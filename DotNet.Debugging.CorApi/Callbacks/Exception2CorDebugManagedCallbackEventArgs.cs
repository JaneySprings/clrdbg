namespace DotNet.Debugging.CorApi;

public sealed class Exception2CorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public CorDebugExceptionCallbackType DwEventType { get; }
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public ICorDebugFrame Frame { get; }
    public uint NOffset { get; }
    public uint DwFlags { get; }

    public Exception2CorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFrame pFrame, uint nOffset, CorDebugExceptionCallbackType dwEventType, uint dwFlags) {
        AppDomain = pAppDomain;
        Thread = pThread;
        Frame = pFrame;
        NOffset = nOffset;
        DwEventType = dwEventType;
        DwFlags = dwFlags;
    }
}