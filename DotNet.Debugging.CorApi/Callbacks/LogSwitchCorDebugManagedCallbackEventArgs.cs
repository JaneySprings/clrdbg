namespace DotNet.Debugging.CorApi;

public sealed class LogSwitchCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public int LLevel { get; }
    public uint UlReason { get; }
    public string LogSwitchName { get; }
    public string ParentName { get; }

    public LogSwitchCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, uint ulReason, string pLogSwitchName, string pParentName) {
        AppDomain = pAppDomain;
        Thread = pThread;
        LLevel = lLevel;
        UlReason = ulReason;
        LogSwitchName = pLogSwitchName;
        ParentName = pParentName;
    }
}