#include <mutex>
#include "Debugger/Callbacks.h"
#include "Debugger/Handles.h"
#include "Log.h"
#include "Protocol/Connection.h"
#include "Protocol/Protocol.h"

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcontroller-continue-method
static const int ICorDebugController_Continue = 4;
// The process and the host connection (a socket) its CreateProcess callback was reported to, -1 for none; the callback
// thread writes them, the agent thread reads them for Hello
static std::mutex processLock;
static void* debuggedProcess = NULL;
static int attachHost = -1;

void* GetDebuggedProcess() {
    std::lock_guard<std::mutex> guard(processLock);
    return debuggedProcess;
}
uint32_t GetProcessHandleForReplay() {
    std::lock_guard<std::mutex> guard(processLock);
    if (debuggedProcess == NULL || attachHost == CurrentHost())
        return 0;
    return RegisterObject(debuggedProcess, false);
}
void ContinueController(void* controller, const char* name) {
    if (controller == NULL)
        controller = GetDebuggedProcess();
    if (controller == NULL)
        return;
    HRESULT hr = CallMethod(controller, ICorDebugController_Continue, (Word)0);
    if (hr != S_OK)
        Log("  Continue after %s failed: 0x%08x", name, (unsigned)hr);
}

// ---- reporting a callback: begin the event, write its arguments, send it -----------------------------------------
// A callback with no host to report to is continued at once, without touching the handle table
static bool BeginCallback(ByteWriter& body, CallbackId id, const char* name, void* controller) {
    if (!HostConnected()) {
        Log("callback %s (no host)", name);
        ContinueController(controller, name);
        return false;
    }
    body.WriteUInt16(Event_Callback);
    body.WriteUInt16((uint16_t)id);
    return true;
}
static void WriteHandle(ByteWriter& body, void* object) {
    body.WriteUInt32(RegisterObject(object, false));
}
static HRESULT SendCallback(ByteWriter& body, const char* name, void* controller) {
    Log("callback %s", name);
    if (SendFrame(FrameKind_Event, 0, body))
        return S_OK;
    ContinueController(controller, name);
    return S_OK;
}
// The common shapes: a controller and an object, a controller and two objects, and so on
static HRESULT ReportTwo(CallbackId id, const char* name, void* controller, void* second) {
    ByteWriter body;
    if (!BeginCallback(body, id, name, controller))
        return S_OK;
    WriteHandle(body, controller);
    WriteHandle(body, second);
    return SendCallback(body, name, controller);
}
static HRESULT ReportThree(CallbackId id, const char* name, void* controller, void* second, void* third) {
    ByteWriter body;
    if (!BeginCallback(body, id, name, controller))
        return S_OK;
    WriteHandle(body, controller);
    WriteHandle(body, second);
    WriteHandle(body, third);
    return SendCallback(body, name, controller);
}
static HRESULT ReportThreeAndNumber(CallbackId id, const char* name, void* controller, void* second, void* third, uint32_t number) {
    ByteWriter body;
    if (!BeginCallback(body, id, name, controller))
        return S_OK;
    WriteHandle(body, controller);
    WriteHandle(body, second);
    WriteHandle(body, third);
    body.WriteUInt32(number);
    return SendCallback(body, name, controller);
}

// ---- ICorDebugManagedCallback, in vtable order ---------------------------------------------------------------------
// Each callback has its page under https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/, named
// icordebugmanagedcallback-<callback>-method; the argument order below is the interface's (cordebug.idl)
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-breakpoint-method
static HRESULT OnBreakpoint(void* self, void* domain, void* thread, void* breakpoint) {
    return ReportThree(CallbackId_Breakpoint, "Breakpoint", domain, thread, breakpoint);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-stepcomplete-method
static HRESULT OnStepComplete(void* self, void* domain, void* thread, void* stepper, uint32_t reason) {
    return ReportThreeAndNumber(CallbackId_StepComplete, "StepComplete", domain, thread, stepper, reason);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-break-method
static HRESULT OnBreak(void* self, void* domain, void* thread) {
    return ReportTwo(CallbackId_Break, "Break", domain, thread);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-exception-method
static HRESULT OnException(void* self, void* domain, void* thread, BOOL unhandled) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_Exception, "Exception", domain))
        return S_OK;
    WriteHandle(body, domain);
    WriteHandle(body, thread);
    body.WriteUInt32((uint32_t)unhandled);
    return SendCallback(body, "Exception", domain);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-evalcomplete-method
static HRESULT OnEvalComplete(void* self, void* domain, void* thread, void* eval) {
    return ReportThree(CallbackId_EvalComplete, "EvalComplete", domain, thread, eval);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-evalexception-method
static HRESULT OnEvalException(void* self, void* domain, void* thread, void* eval) {
    return ReportThree(CallbackId_EvalException, "EvalException", domain, thread, eval);
}
// The first callback of the attach; the process it brings is the one every controller-less stop is continued through
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-createprocess-method
static HRESULT OnCreateProcess(void* self, void* process) {
    AddRef(process);
    {
        std::lock_guard<std::mutex> guard(processLock);
        debuggedProcess = process;
        attachHost = CurrentHost();
    }
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_CreateProcess, "CreateProcess", process))
        return S_OK;
    WriteHandle(body, process);
    return SendCallback(body, "CreateProcess", process);
}
// The last callback: nothing is left to continue
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-exitprocess-method
static HRESULT OnExitProcess(void* self, void* process) {
    Log("callback ExitProcess (not continued)");
    if (!HostConnected())
        return S_OK;
    ByteWriter body;
    body.WriteUInt16(Event_Callback);
    body.WriteUInt16((uint16_t)CallbackId_ExitProcess);
    WriteHandle(body, process);
    SendFrame(FrameKind_Event, 0, body);
    return S_OK;
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-createthread-method
static HRESULT OnCreateThread(void* self, void* domain, void* thread) {
    return ReportTwo(CallbackId_CreateThread, "CreateThread", domain, thread);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-exitthread-method
static HRESULT OnExitThread(void* self, void* domain, void* thread) {
    return ReportTwo(CallbackId_ExitThread, "ExitThread", domain, thread);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-loadmodule-method
static HRESULT OnLoadModule(void* self, void* domain, void* module) {
    return ReportTwo(CallbackId_LoadModule, "LoadModule", domain, module);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-unloadmodule-method
static HRESULT OnUnloadModule(void* self, void* domain, void* module) {
    return ReportTwo(CallbackId_UnloadModule, "UnloadModule", domain, module);
}
// Raised only for the modules the debugger enabled class load callbacks on (dynamic modules)
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-loadclass-method
static HRESULT OnLoadClass(void* self, void* domain, void* type) {
    return ReportTwo(CallbackId_LoadClass, "LoadClass", domain, type);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-unloadclass-method
static HRESULT OnUnloadClass(void* self, void* domain, void* type) {
    return ReportTwo(CallbackId_UnloadClass, "UnloadClass", domain, type);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-debuggererror-method
static HRESULT OnDebuggerError(void* self, void* process, HRESULT error, DWORD code) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_DebuggerError, "DebuggerError", process))
        return S_OK;
    WriteHandle(body, process);
    body.WriteUInt32((uint32_t)error);
    body.WriteUInt32(code);
    return SendCallback(body, "DebuggerError", process);
}
// Debug.WriteLine and friends, once the debugger called ICorDebugProcess::EnableLogMessages
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-logmessage-method
static HRESULT OnLogMessage(void* self, void* domain, void* thread, int32_t level, const WCHAR* switchName, const WCHAR* message) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_LogMessage, "LogMessage", domain))
        return S_OK;
    WriteHandle(body, domain);
    WriteHandle(body, thread);
    body.WriteUInt32((uint32_t)level);
    body.WriteWideString(switchName);
    body.WriteWideString(message);
    return SendCallback(body, "LogMessage", domain);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-logswitch-method
static HRESULT OnLogSwitch(void* self, void* domain, void* thread, int32_t level, ULONG reason, const WCHAR* switchName, const WCHAR* parentName) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_LogSwitch, "LogSwitch", domain))
        return S_OK;
    WriteHandle(body, domain);
    WriteHandle(body, thread);
    body.WriteUInt32((uint32_t)level);
    body.WriteUInt32(reason);
    body.WriteWideString(switchName);
    body.WriteWideString(parentName);
    return SendCallback(body, "LogSwitch", domain);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-createappdomain-method
static HRESULT OnCreateAppDomain(void* self, void* process, void* domain) {
    return ReportTwo(CallbackId_CreateAppDomain, "CreateAppDomain", process, domain);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-exitappdomain-method
static HRESULT OnExitAppDomain(void* self, void* process, void* domain) {
    return ReportTwo(CallbackId_ExitAppDomain, "ExitAppDomain", process, domain);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-loadassembly-method
static HRESULT OnLoadAssembly(void* self, void* domain, void* assembly) {
    return ReportTwo(CallbackId_LoadAssembly, "LoadAssembly", domain, assembly);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-unloadassembly-method
static HRESULT OnUnloadAssembly(void* self, void* domain, void* assembly) {
    return ReportTwo(CallbackId_UnloadAssembly, "UnloadAssembly", domain, assembly);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-controlctrap-method
static HRESULT OnControlCTrap(void* self, void* process) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_ControlCTrap, "ControlCTrap", process))
        return S_OK;
    WriteHandle(body, process);
    return SendCallback(body, "ControlCTrap", process);
}
// A thread rename arrives with a null app domain, which ContinueController maps to the process
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-namechange-method
static HRESULT OnNameChange(void* self, void* domain, void* thread) {
    return ReportTwo(CallbackId_NameChange, "NameChange", domain, thread);
}
// The symbol stream is not carried
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-updatemodulesymbols-method
static HRESULT OnUpdateModuleSymbols(void* self, void* domain, void* module, void* stream) {
    return ReportTwo(CallbackId_UpdateModuleSymbols, "UpdateModuleSymbols", domain, module);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-editandcontinueremap-method
static HRESULT OnEditAndContinueRemap(void* self, void* domain, void* thread, void* function, BOOL accurate) {
    return ReportThreeAndNumber(CallbackId_EditAndContinueRemap, "EditAndContinueRemap", domain, thread, function, (uint32_t)accurate);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-breakpointseterror-method
static HRESULT OnBreakpointSetError(void* self, void* domain, void* thread, void* breakpoint, DWORD error) {
    return ReportThreeAndNumber(CallbackId_BreakpointSetError, "BreakpointSetError", domain, thread, breakpoint, error);
}

// ---- ICorDebugManagedCallback2, in vtable order --------------------------------------------------------------------
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback2-functionremapopportunity-method
static HRESULT OnFunctionRemapOpportunity(void* self, void* domain, void* thread, void* oldFunction, void* newFunction, ULONG oldOffset) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_FunctionRemapOpportunity, "FunctionRemapOpportunity", domain))
        return S_OK;
    WriteHandle(body, domain);
    WriteHandle(body, thread);
    WriteHandle(body, oldFunction);
    WriteHandle(body, newFunction);
    body.WriteUInt32(oldOffset);
    return SendCallback(body, "FunctionRemapOpportunity", domain);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback2-createconnection-method
static HRESULT OnCreateConnection(void* self, void* process, DWORD connectionId, const WCHAR* name) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_CreateConnection, "CreateConnection", process))
        return S_OK;
    WriteHandle(body, process);
    body.WriteUInt32(connectionId);
    body.WriteWideString(name);
    return SendCallback(body, "CreateConnection", process);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback2-changeconnection-method
static HRESULT OnChangeConnection(void* self, void* process, DWORD connectionId) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_ChangeConnection, "ChangeConnection", process))
        return S_OK;
    WriteHandle(body, process);
    body.WriteUInt32(connectionId);
    return SendCallback(body, "ChangeConnection", process);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback2-destroyconnection-method
static HRESULT OnDestroyConnection(void* self, void* process, DWORD connectionId) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_DestroyConnection, "DestroyConnection", process))
        return S_OK;
    WriteHandle(body, process);
    body.WriteUInt32(connectionId);
    return SendCallback(body, "DestroyConnection", process);
}
// The exception dispatch phases (first chance, user first chance, catch handler found, unhandled)
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback2-exception-method
static HRESULT OnException2(void* self, void* domain, void* thread, void* frame, ULONG offset, uint32_t eventType, DWORD flags) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_Exception2, "Exception2", domain))
        return S_OK;
    WriteHandle(body, domain);
    WriteHandle(body, thread);
    WriteHandle(body, frame);
    body.WriteUInt32(offset);
    body.WriteUInt32(eventType);
    body.WriteUInt32(flags);
    return SendCallback(body, "Exception2", domain);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback2-exceptionunwind-method
static HRESULT OnExceptionUnwind(void* self, void* domain, void* thread, uint32_t eventType, DWORD flags) {
    ByteWriter body;
    if (!BeginCallback(body, CallbackId_ExceptionUnwind, "ExceptionUnwind", domain))
        return S_OK;
    WriteHandle(body, domain);
    WriteHandle(body, thread);
    body.WriteUInt32(eventType);
    body.WriteUInt32(flags);
    return SendCallback(body, "ExceptionUnwind", domain);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback2-functionremapcomplete-method
static HRESULT OnFunctionRemapComplete(void* self, void* domain, void* thread, void* function) {
    return ReportThree(CallbackId_FunctionRemapComplete, "FunctionRemapComplete", domain, thread, function);
}
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback2-mdanotification-method
static HRESULT OnMDANotification(void* self, void* controller, void* thread, void* mda) {
    return ReportThree(CallbackId_MDANotification, "MDANotification", controller, thread, mda);
}

// ---- the two callback objects: static, never freed, so AddRef and Release do nothing ------------------------------
// SetManagedHandler queries the handler for ICorDebugManagedCallback2 (Cordb::SetManagedHandler in
// src/coreclr/debug/di/cordb.cpp), so the first object answers for the second and back
static HRESULT QueryInterfaceCallback(void* self, const GUID* iid, void** result);
static HRESULT QueryInterfaceCallback2(void* self, const GUID* iid, void** result);
static ULONG AddRefStatic(void* self) {
    return 1;
}
static ULONG ReleaseStatic(void* self) {
    return 1;
}

static const Method managedCallbackVtable[] = {
    (Method)QueryInterfaceCallback, (Method)AddRefStatic, (Method)ReleaseStatic,
    (Method)OnBreakpoint, (Method)OnStepComplete, (Method)OnBreak, (Method)OnException,
    (Method)OnEvalComplete, (Method)OnEvalException, (Method)OnCreateProcess, (Method)OnExitProcess,
    (Method)OnCreateThread, (Method)OnExitThread, (Method)OnLoadModule, (Method)OnUnloadModule,
    (Method)OnLoadClass, (Method)OnUnloadClass, (Method)OnDebuggerError, (Method)OnLogMessage,
    (Method)OnLogSwitch, (Method)OnCreateAppDomain, (Method)OnExitAppDomain, (Method)OnLoadAssembly,
    (Method)OnUnloadAssembly, (Method)OnControlCTrap, (Method)OnNameChange, (Method)OnUpdateModuleSymbols,
    (Method)OnEditAndContinueRemap, (Method)OnBreakpointSetError,
};
static const Method managedCallback2Vtable[] = {
    (Method)QueryInterfaceCallback2, (Method)AddRefStatic, (Method)ReleaseStatic,
    (Method)OnFunctionRemapOpportunity, (Method)OnCreateConnection, (Method)OnChangeConnection,
    (Method)OnDestroyConnection, (Method)OnException2, (Method)OnExceptionUnwind,
    (Method)OnFunctionRemapComplete, (Method)OnMDANotification,
};
static ComObject managedCallbackObject = { managedCallbackVtable };
static ComObject managedCallback2Object = { managedCallback2Vtable };

static HRESULT QueryInterfaceCallback(void* self, const GUID* iid, void** result) {
    if (result == NULL)
        return E_POINTER;
    if (SameGuid(iid, &IID_IUnknown) || SameGuid(iid, &IID_ICorDebugManagedCallback)) {
        *result = self;
        return S_OK;
    }
    if (SameGuid(iid, &IID_ICorDebugManagedCallback2)) {
        *result = &managedCallback2Object;
        return S_OK;
    }
    *result = NULL;
    return E_NOINTERFACE;
}
static HRESULT QueryInterfaceCallback2(void* self, const GUID* iid, void** result) {
    if (result == NULL)
        return E_POINTER;
    if (SameGuid(iid, &IID_ICorDebugManagedCallback2)) {
        *result = self;
        return S_OK;
    }
    return QueryInterfaceCallback(&managedCallbackObject, iid, result);
}
void* GetManagedCallback() {
    return &managedCallbackObject;
}
