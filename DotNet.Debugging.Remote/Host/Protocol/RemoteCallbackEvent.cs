namespace DotNet.Debugging.Remote.Protocol;

// A callback the agent reported, with its arguments still to be read in the callback's order
public class RemoteCallbackEvent {
    public RemoteCallback Id { get; }
    public ByteReader Data { get; }

    public RemoteCallbackEvent(RemoteCallback id, ByteReader data) {
        Id = id;
        Data = data;
    }
}
