namespace DotNet.Debugging.Remote.Protocol;

// The kinds of frame on the wire: a request travels host to agent, a response answers it with the same sequence,
// an event travels agent to host on its own (sequence 0)
public enum RemoteFrameKind : byte {
    Request = 1,
    Response = 2,
    Event = 3,
}
