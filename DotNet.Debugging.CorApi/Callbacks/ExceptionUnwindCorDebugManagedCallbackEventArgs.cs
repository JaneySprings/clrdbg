namespace DotNet.Debugging.CorApi;

public sealed class ExceptionUnwindCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public CorDebugExceptionUnwindCallbackType DwEventType { get; }
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public uint DwFlags { get; }

    public ExceptionUnwindCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, CorDebugExceptionUnwindCallbackType dwEventType, uint dwFlags) {
        AppDomain = pAppDomain;
        Thread = pThread;
        DwEventType = dwEventType;
        DwFlags = dwFlags;
    }
}