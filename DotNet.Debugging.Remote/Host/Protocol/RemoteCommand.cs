namespace DotNet.Debugging.Remote.Protocol;

public enum RemoteCommand : ushort {
    Hello = 1,
    Invoke = 2,
    Release = 3,
    Terminate = 4,
    Query = 5,
}
