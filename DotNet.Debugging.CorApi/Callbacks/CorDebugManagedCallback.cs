using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComClass]
public partial class CorDebugManagedCallback : ICorDebugManagedCallback, ICorDebugManagedCallback3, ICorDebugManagedCallback4, ICorDebugManagedCallback2 {
    public event EventHandler<CorDebugManagedCallbackEventArgs>? OnAnyEvent;

    public event EventHandler<BreakpointCorDebugManagedCallbackEventArgs>? OnBreakpoint;

    public event EventHandler<StepCompleteCorDebugManagedCallbackEventArgs>? OnStepComplete;

    public event EventHandler<BreakCorDebugManagedCallbackEventArgs>? OnBreak;

    public event EventHandler<ExceptionCorDebugManagedCallbackEventArgs>? OnException;

    public event EventHandler<EvalCompleteCorDebugManagedCallbackEventArgs>? OnEvalComplete;

    public event EventHandler<EvalExceptionCorDebugManagedCallbackEventArgs>? OnEvalException;

    public event EventHandler<CreateProcessCorDebugManagedCallbackEventArgs>? OnCreateProcess;

    public event EventHandler<ExitProcessCorDebugManagedCallbackEventArgs>? OnExitProcess;

    public event EventHandler<CreateThreadCorDebugManagedCallbackEventArgs>? OnCreateThread;

    public event EventHandler<ExitThreadCorDebugManagedCallbackEventArgs>? OnExitThread;

    public event EventHandler<LoadModuleCorDebugManagedCallbackEventArgs>? OnLoadModule;

    public event EventHandler<UnloadModuleCorDebugManagedCallbackEventArgs>? OnUnloadModule;

    public event EventHandler<LoadClassCorDebugManagedCallbackEventArgs>? OnLoadClass;

    public event EventHandler<UnloadClassCorDebugManagedCallbackEventArgs>? OnUnloadClass;

    public event EventHandler<DebuggerErrorCorDebugManagedCallbackEventArgs>? OnDebuggerError;

    public event EventHandler<LogMessageCorDebugManagedCallbackEventArgs>? OnLogMessage;

    public event EventHandler<LogSwitchCorDebugManagedCallbackEventArgs>? OnLogSwitch;

    public event EventHandler<CreateAppDomainCorDebugManagedCallbackEventArgs>? OnCreateAppDomain;

    public event EventHandler<ExitAppDomainCorDebugManagedCallbackEventArgs>? OnExitAppDomain;

    public event EventHandler<LoadAssemblyCorDebugManagedCallbackEventArgs>? OnLoadAssembly;

    public event EventHandler<UnloadAssemblyCorDebugManagedCallbackEventArgs>? OnUnloadAssembly;

    public event EventHandler<ControlCTrapCorDebugManagedCallbackEventArgs>? OnControlCTrap;

    public event EventHandler<NameChangeCorDebugManagedCallbackEventArgs>? OnNameChange;

    public event EventHandler<UpdateModuleSymbolsCorDebugManagedCallbackEventArgs>? OnUpdateModuleSymbols;

    public event EventHandler<EditAndContinueRemapCorDebugManagedCallbackEventArgs>? OnEditAndContinueRemap;

    public event EventHandler<BreakpointSetErrorCorDebugManagedCallbackEventArgs>? OnBreakpointSetError;

    public event EventHandler<CustomNotificationCorDebugManagedCallbackEventArgs>? OnCustomNotification;

    public event EventHandler<BeforeGarbageCollectionCorDebugManagedCallbackEventArgs>? OnBeforeGarbageCollection;

    public event EventHandler<AfterGarbageCollectionCorDebugManagedCallbackEventArgs>? OnAfterGarbageCollection;

    public event EventHandler<DataBreakpointCorDebugManagedCallbackEventArgs>? OnDataBreakpoint;

    public event EventHandler<FunctionRemapOpportunityCorDebugManagedCallbackEventArgs>? OnFunctionRemapOpportunity;

    public event EventHandler<CreateConnectionCorDebugManagedCallbackEventArgs>? OnCreateConnection;

    public event EventHandler<ChangeConnectionCorDebugManagedCallbackEventArgs>? OnChangeConnection;

    public event EventHandler<DestroyConnectionCorDebugManagedCallbackEventArgs>? OnDestroyConnection;

    public event EventHandler<Exception2CorDebugManagedCallbackEventArgs>? OnException2;

    public event EventHandler<ExceptionUnwindCorDebugManagedCallbackEventArgs>? OnExceptionUnwind;

    public event EventHandler<FunctionRemapCompleteCorDebugManagedCallbackEventArgs>? OnFunctionRemapComplete;

    public event EventHandler<MDANotificationCorDebugManagedCallbackEventArgs>? OnMDANotification;

    private int HandleEvent<TEventArgs>(EventHandler<TEventArgs>? handler, TEventArgs args) where TEventArgs : CorDebugManagedCallbackEventArgs {
        handler?.Invoke(this, args);
        OnAnyEvent?.Invoke(this, args);
        return 0;
    }

    int ICorDebugManagedCallback.TryBreakpoint(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint) {
        return HandleEvent(OnBreakpoint, new BreakpointCorDebugManagedCallbackEventArgs(pAppDomain, pThread, pBreakpoint));
    }

    int ICorDebugManagedCallback.TryStepComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugStepper pStepper, CorDebugStepReason reason) {
        return HandleEvent(OnStepComplete, new StepCompleteCorDebugManagedCallbackEventArgs(pAppDomain, pThread, pStepper, reason));
    }

    int ICorDebugManagedCallback.TryBreak(ICorDebugAppDomain pAppDomain, ICorDebugThread thread) {
        return HandleEvent(OnBreak, new BreakCorDebugManagedCallbackEventArgs(pAppDomain, thread));
    }

    int ICorDebugManagedCallback.TryException(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, [MarshalAs(UnmanagedType.Bool)] bool unhandled) {
        return HandleEvent(OnException, new ExceptionCorDebugManagedCallbackEventArgs(pAppDomain, pThread, unhandled));
    }

    int ICorDebugManagedCallback.TryEvalComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval) {
        return HandleEvent(OnEvalComplete, new EvalCompleteCorDebugManagedCallbackEventArgs(pAppDomain, pThread, pEval));
    }

    int ICorDebugManagedCallback.TryEvalException(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval) {
        return HandleEvent(OnEvalException, new EvalExceptionCorDebugManagedCallbackEventArgs(pAppDomain, pThread, pEval));
    }

    int ICorDebugManagedCallback.TryCreateProcess(ICorDebugProcess pProcess) {
        return HandleEvent(OnCreateProcess, new CreateProcessCorDebugManagedCallbackEventArgs(pProcess));
    }

    int ICorDebugManagedCallback.TryExitProcess(ICorDebugProcess pProcess) {
        return HandleEvent(OnExitProcess, new ExitProcessCorDebugManagedCallbackEventArgs(pProcess));
    }

    int ICorDebugManagedCallback.TryCreateThread(ICorDebugAppDomain pAppDomain, ICorDebugThread thread) {
        return HandleEvent(OnCreateThread, new CreateThreadCorDebugManagedCallbackEventArgs(pAppDomain, thread));
    }

    int ICorDebugManagedCallback.TryExitThread(ICorDebugAppDomain pAppDomain, ICorDebugThread thread) {
        return HandleEvent(OnExitThread, new ExitThreadCorDebugManagedCallbackEventArgs(pAppDomain, thread));
    }

    int ICorDebugManagedCallback.TryLoadModule(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule) {
        return HandleEvent(OnLoadModule, new LoadModuleCorDebugManagedCallbackEventArgs(pAppDomain, pModule));
    }

    int ICorDebugManagedCallback.TryUnloadModule(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule) {
        return HandleEvent(OnUnloadModule, new UnloadModuleCorDebugManagedCallbackEventArgs(pAppDomain, pModule));
    }

    int ICorDebugManagedCallback.TryLoadClass(ICorDebugAppDomain pAppDomain, ICorDebugClass c) {
        return HandleEvent(OnLoadClass, new LoadClassCorDebugManagedCallbackEventArgs(pAppDomain, c));
    }

    int ICorDebugManagedCallback.TryUnloadClass(ICorDebugAppDomain pAppDomain, ICorDebugClass c) {
        return HandleEvent(OnUnloadClass, new UnloadClassCorDebugManagedCallbackEventArgs(pAppDomain, c));
    }

    int ICorDebugManagedCallback.TryDebuggerError(ICorDebugProcess pProcess, int errorHR, uint errorCode) {
        return HandleEvent(OnDebuggerError, new DebuggerErrorCorDebugManagedCallbackEventArgs(pProcess, errorHR, errorCode));
    }

    int ICorDebugManagedCallback.TryLogMessage(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, string pLogSwitchName, string pMessage) {
        return HandleEvent(OnLogMessage, new LogMessageCorDebugManagedCallbackEventArgs(pAppDomain, pThread, lLevel, pLogSwitchName, pMessage));
    }

    int ICorDebugManagedCallback.TryLogSwitch(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, uint ulReason, string pLogSwitchName, string pParentName) {
        return HandleEvent(OnLogSwitch, new LogSwitchCorDebugManagedCallbackEventArgs(pAppDomain, pThread, lLevel, ulReason, pLogSwitchName, pParentName));
    }

    int ICorDebugManagedCallback.TryCreateAppDomain(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain) {
        return HandleEvent(OnCreateAppDomain, new CreateAppDomainCorDebugManagedCallbackEventArgs(pProcess, pAppDomain));
    }

    int ICorDebugManagedCallback.TryExitAppDomain(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain) {
        return HandleEvent(OnExitAppDomain, new ExitAppDomainCorDebugManagedCallbackEventArgs(pProcess, pAppDomain));
    }

    int ICorDebugManagedCallback.TryLoadAssembly(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly) {
        return HandleEvent(OnLoadAssembly, new LoadAssemblyCorDebugManagedCallbackEventArgs(pAppDomain, pAssembly));
    }

    int ICorDebugManagedCallback.TryUnloadAssembly(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly) {
        return HandleEvent(OnUnloadAssembly, new UnloadAssemblyCorDebugManagedCallbackEventArgs(pAppDomain, pAssembly));
    }

    int ICorDebugManagedCallback.TryControlCTrap(ICorDebugProcess pProcess) {
        return HandleEvent(OnControlCTrap, new ControlCTrapCorDebugManagedCallbackEventArgs(pProcess));
    }

    int ICorDebugManagedCallback.TryNameChange(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread) {
        return HandleEvent(OnNameChange, new NameChangeCorDebugManagedCallbackEventArgs(pAppDomain, pThread));
    }

    int ICorDebugManagedCallback.TryUpdateModuleSymbols(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule, nint pSymbolStream) {
        return HandleEvent(OnUpdateModuleSymbols, new UpdateModuleSymbolsCorDebugManagedCallbackEventArgs(pAppDomain, pModule, pSymbolStream));
    }

    int ICorDebugManagedCallback.TryEditAndContinueRemap(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction, [MarshalAs(UnmanagedType.Bool)] bool fAccurate) {
        return HandleEvent(OnEditAndContinueRemap, new EditAndContinueRemapCorDebugManagedCallbackEventArgs(pAppDomain, pThread, pFunction, fAccurate));
    }

    int ICorDebugManagedCallback.TryBreakpointSetError(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint, uint dwError) {
        return HandleEvent(OnBreakpointSetError, new BreakpointSetErrorCorDebugManagedCallbackEventArgs(pAppDomain, pThread, pBreakpoint, dwError));
    }

    int ICorDebugManagedCallback3.TryCustomNotification(ICorDebugThread pThread, ICorDebugAppDomain pAppDomain) {
        return HandleEvent(OnCustomNotification, new CustomNotificationCorDebugManagedCallbackEventArgs(pThread, pAppDomain));
    }

    int ICorDebugManagedCallback4.TryBeforeGarbageCollection(ICorDebugProcess pProcess) {
        return HandleEvent(OnBeforeGarbageCollection, new BeforeGarbageCollectionCorDebugManagedCallbackEventArgs(pProcess));
    }

    int ICorDebugManagedCallback4.TryAfterGarbageCollection(ICorDebugProcess pProcess) {
        return HandleEvent(OnAfterGarbageCollection, new AfterGarbageCollectionCorDebugManagedCallbackEventArgs(pProcess));
    }

    int ICorDebugManagedCallback4.TryDataBreakpoint(ICorDebugProcess pProcess, ICorDebugThread pThread, ref byte pContext, uint contextSize) {
        return HandleEvent(OnDataBreakpoint, new DataBreakpointCorDebugManagedCallbackEventArgs(pProcess, pThread, pContext, contextSize));
    }

    int ICorDebugManagedCallback2.TryFunctionRemapOpportunity(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pOldFunction, ICorDebugFunction pNewFunction, uint oldILOffset) {
        return HandleEvent(OnFunctionRemapOpportunity, new FunctionRemapOpportunityCorDebugManagedCallbackEventArgs(pAppDomain, pThread, pOldFunction, pNewFunction, oldILOffset));
    }

    int ICorDebugManagedCallback2.TryCreateConnection(ICorDebugProcess pProcess, uint dwConnectionId, string pConnName) {
        return HandleEvent(OnCreateConnection, new CreateConnectionCorDebugManagedCallbackEventArgs(pProcess, dwConnectionId, pConnName));
    }

    int ICorDebugManagedCallback2.TryChangeConnection(ICorDebugProcess pProcess, uint dwConnectionId) {
        return HandleEvent(OnChangeConnection, new ChangeConnectionCorDebugManagedCallbackEventArgs(pProcess, dwConnectionId));
    }

    int ICorDebugManagedCallback2.TryDestroyConnection(ICorDebugProcess pProcess, uint dwConnectionId) {
        return HandleEvent(OnDestroyConnection, new DestroyConnectionCorDebugManagedCallbackEventArgs(pProcess, dwConnectionId));
    }

    int ICorDebugManagedCallback2.TryException(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFrame pFrame, uint nOffset, CorDebugExceptionCallbackType dwEventType, uint dwFlags) {
        return HandleEvent(OnException2, new Exception2CorDebugManagedCallbackEventArgs(pAppDomain, pThread, pFrame, nOffset, dwEventType, dwFlags));
    }

    int ICorDebugManagedCallback2.TryExceptionUnwind(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, CorDebugExceptionUnwindCallbackType dwEventType, uint dwFlags) {
        return HandleEvent(OnExceptionUnwind, new ExceptionUnwindCorDebugManagedCallbackEventArgs(pAppDomain, pThread, dwEventType, dwFlags));
    }

    int ICorDebugManagedCallback2.TryFunctionRemapComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction) {
        return HandleEvent(OnFunctionRemapComplete, new FunctionRemapCompleteCorDebugManagedCallbackEventArgs(pAppDomain, pThread, pFunction));
    }

    int ICorDebugManagedCallback2.TryMDANotification(ICorDebugController pController, ICorDebugThread pThread, ICorDebugMDA pMDA) {
        return HandleEvent(OnMDANotification, new MDANotificationCorDebugManagedCallbackEventArgs(pController, pThread, pMDA));
    }
}