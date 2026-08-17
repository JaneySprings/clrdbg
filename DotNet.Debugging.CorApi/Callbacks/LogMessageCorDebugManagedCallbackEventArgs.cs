namespace DotNet.Debugging.CorApi;

public sealed class LogMessageCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public int LLevel { get; }
    public string LogSwitchName { get; }
    public string Message { get; }

    public LogMessageCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, string pLogSwitchName, string pMessage) {
        AppDomain = pAppDomain;
        Thread = pThread;
        LLevel = lLevel;
        LogSwitchName = pLogSwitchName;
        Message = pMessage;
    }
}