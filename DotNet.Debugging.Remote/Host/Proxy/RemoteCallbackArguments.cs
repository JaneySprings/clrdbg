using DotNet.Debugging.Remote.Protocol;

namespace DotNet.Debugging.Remote.Proxy;

// The arguments of a callback event, read in the callback's order: the handles come first (the first one is the
// controller the callback stopped, zero for a thread's NameChange without an app domain), then the numbers and texts.
// A handle read as a proxy is the proxy's to give back; the ones of a callback the debugger is not handed are given
// back here
public class RemoteCallbackArguments {
    private readonly RemoteSession session;
    private readonly ByteReader reader;
    private int handlesRead;

    public uint ControllerHandle { get; private set; }

    public RemoteCallbackArguments(RemoteSession session, ByteReader reader) {
        this.session = session;
        this.reader = reader;
    }

    public T? Proxy<T>() where T : class {
        return session.GetProxy<T>(Handle());
    }
    public uint Handle() {
        var handle = reader.ReadUInt32();
        if (handlesRead == 0)
            ControllerHandle = handle;
        handlesRead++;
        return handle;
    }
    public uint UInt32() {
        return reader.ReadUInt32();
    }
    public string Text() {
        return reader.ReadWide();
    }
    // Gives back the handles not read yet, of the 'total' the callback carries
    public void DiscardRemaining(int total) {
        while (handlesRead < total)
            session.Discard(Handle());
    }
}
