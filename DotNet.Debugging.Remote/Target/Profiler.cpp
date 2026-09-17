// The profiler stub the runtime loads: an IClassFactory handing out an ICorProfilerCallback2 that does nothing. It
// profiles nothing, sets no event mask and answers every profiler callback with S_OK; its Initialize only starts the
// agent thread, before any managed code runs. The runtime reads CORECLR_ENABLE_PROFILING, CORECLR_PROFILER (any GUID
// will do, the factory ignores it) and CORECLR_PROFILER_PATH in ProfilingAPIUtility::AttemptLoadProfilerForStartup
// and LoadProfiler (src/coreclr/vm/profilinghelper.cpp), then EEToProfInterfaceImpl::CreateProfiler
// (src/coreclr/vm/eetoprofinterfaceimpl.cpp) calls the library's DllGetClassObject and the factory's CreateInstance.
// EEStartupHelper (src/coreclr/vm/ceemain.cpp) runs InitializeDebugger before InitializeProfiling, which is why the
// agent thread finds a debugger transport to attach to
// https://learn.microsoft.com/dotnet/core/runtime-config/debugging-profiling
// https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/profilinghelper.cpp
// https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/eetoprofinterfaceimpl.cpp
// https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/ceemain.cpp
#include "Agent.h"
#include "Com.h"
#include "Log.h"

// https://learn.microsoft.com/dotnet/framework/unmanaged-api/profiling/icorprofilercallback-initialize-method
static HRESULT ProfilerInitialize(void* self, void* profilerInfo) {
    Log("profiler Initialize: the runtime loaded the agent; starting the agent thread");
    StartAgentThread();
    return S_OK;
}
// https://learn.microsoft.com/dotnet/framework/unmanaged-api/profiling/icorprofilercallback-shutdown-method
static HRESULT ProfilerShutdown(void* self) {
    Log("profiler Shutdown");
    return S_OK;
}
// Answers any profiler callback, whatever its arguments
static HRESULT StubOk(void* self, ...) {
    return S_OK;
}
static ULONG AddRefStatic(void* self) {
    return 1;
}
static ULONG ReleaseStatic(void* self) {
    return 1;
}

// ICorProfilerCallback2 has 110 slots (IUnknown + ICorProfilerCallback + its own); every one but Initialize is a no-op.
// The runtime asks the factory for ICorProfilerCallback2 (queried again afterwards), never for a later version
// https://learn.microsoft.com/dotnet/framework/unmanaged-api/profiling/icorprofilercallback-interface
// https://learn.microsoft.com/dotnet/framework/unmanaged-api/profiling/icorprofilercallback2-interface
static Method profilerVtable[128];
static ComObject profilerObject = { profilerVtable };

static HRESULT ProfilerQueryInterface(void* self, const GUID* iid, void** result) {
    if (result == NULL)
        return E_POINTER;
    if (SameGuid(iid, &IID_IUnknown) || SameGuid(iid, &IID_ICorProfilerCallback) || SameGuid(iid, &IID_ICorProfilerCallback2)) {
        *result = self;
        return S_OK;
    }
    *result = NULL;
    return E_NOINTERFACE;
}
static void InitializeProfilerVtable() {
    for (int i = 0; i < 128; i++)
        profilerVtable[i] = (Method)StubOk;
    profilerVtable[0] = (Method)ProfilerQueryInterface;
    profilerVtable[1] = (Method)AddRefStatic;
    profilerVtable[2] = (Method)ReleaseStatic;
    profilerVtable[3] = (Method)ProfilerInitialize;
    profilerVtable[4] = (Method)ProfilerShutdown;
}

// The class factory: IUnknown, CreateInstance, LockServer
// https://learn.microsoft.com/windows/win32/api/unknwnbase/nn-unknwnbase-iclassfactory
static HRESULT FactoryQueryInterface(void* self, const GUID* iid, void** result) {
    if (result == NULL)
        return E_POINTER;
    if (SameGuid(iid, &IID_IUnknown) || SameGuid(iid, &IID_IClassFactory)) {
        *result = self;
        return S_OK;
    }
    *result = NULL;
    return E_NOINTERFACE;
}
static HRESULT FactoryCreateInstance(void* self, void* outer, const GUID* iid, void** result) {
    return ProfilerQueryInterface(&profilerObject, iid, result);
}
static HRESULT FactoryLockServer(void* self, BOOL lock) {
    return S_OK;
}
static const Method factoryVtable[] = {
    (Method)FactoryQueryInterface, (Method)AddRefStatic, (Method)ReleaseStatic,
    (Method)FactoryCreateInstance, (Method)FactoryLockServer,
};
static ComObject factoryObject = { factoryVtable };

// https://learn.microsoft.com/windows/win32/api/combaseapi/nf-combaseapi-dllgetclassobject
EXPORT HRESULT DllGetClassObject(const GUID* clsid, const GUID* iid, void** result) {
    InitializeProfilerVtable();
    Log("DllGetClassObject");
    return FactoryQueryInterface(&factoryObject, iid, result);
}
// https://learn.microsoft.com/windows/win32/api/combaseapi/nf-combaseapi-dllcanunloadnow
EXPORT HRESULT DllCanUnloadNow() {
    return S_FALSE;
}
