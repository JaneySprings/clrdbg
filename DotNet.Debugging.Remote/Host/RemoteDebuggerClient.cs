using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using DotNet.Debugging.Remote.Protocol;

namespace DotNet.Debugging.Remote;

// The host end of the agent's protocol: sends requests, matches the responses by sequence, and raises the agent's
// callback events. A callback leaves the debuggee stopped until its controller is continued, the way a queued
// callback would
public class RemoteDebuggerClient : IDisposable {
    // How long hand-outs given back are gathered before they go out as one Release request
    private const int ReleaseDelayMilliseconds = 20;

    private readonly TcpClient client;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<ByteReader>> pendingRequests;
    private readonly SemaphoreSlim writeLock;
    // The agent's events, delivered in their order on one thread of the pool, so a handler may send requests of its
    // own; the delivery starts with the first subscriber, events before that wait for it
    private readonly Channel<RemoteCallbackEvent> callbacks;
    // Handle and count pairs waiting to go out together
    private readonly List<uint> pendingReleases;
    private NetworkStream? stream;
    private uint nextSequence;
    private Action<RemoteCallbackEvent>? callbackReceived;
    private bool dispatching;
    private bool releaseScheduled;
    // Set once the connection is gone: a request made after that fails at once instead of waiting for an answer
    private volatile bool lost;

    public event Action<RemoteCallbackEvent>? CallbackReceived {
        add {
            callbackReceived += value;
            EnsureDispatching();
        }
        remove {
            callbackReceived -= value;
        }
    }
    public event Action? Disconnected;

    public RemoteDebuggerClient() : this(new TcpClient()) { }
    private RemoteDebuggerClient(TcpClient client) {
        this.client = client;
        client.NoDelay = true;
        pendingRequests = new ConcurrentDictionary<uint, TaskCompletionSource<ByteReader>>();
        pendingReleases = new List<uint>();
        writeLock = new SemaphoreSlim(1, 1);
        callbacks = Channel.CreateUnbounded<RemoteCallbackEvent>();
    }

    // A client over a connection the agent made to a listening debugger
    public static RemoteDebuggerClient FromConnected(TcpClient connected) {
        var client = new RemoteDebuggerClient(connected);
        client.StartReading();
        return client;
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default) {
        await client.ConnectAsync(host, port, cancellationToken);
        StartReading();
    }
    private void StartReading() {
        stream = client.GetStream();
        _ = Task.Run(ReadFramesAsync, CancellationToken.None);
    }

    public async Task<RemoteAgentInfo> HelloAsync() {
        var (result, reader) = await RequestAsync(RemoteCommand.Hello);
        ThrowIfFailed(result, RemoteCommand.Hello);
        return new RemoteAgentInfo(reader.ReadUInt32(), (int)reader.ReadUInt32(), reader.ReadString(), reader.ReadUInt32());
    }
    // A call on the object behind 'handle': the callee's HRESULT and the out values of the arguments, still to be read
    // into them with 'ReadResults'
    public async Task<(int HResult, byte[] Results)> InvokeAsync(uint handle, Guid iid, ushort slot, RemoteArgument[] arguments) {
        var (result, reader) = await RequestAsync(RemoteCommand.Invoke, writer => {
            writer.WriteUInt32(handle);
            writer.WriteGuid(iid);
            writer.WriteUInt16(slot);
            writer.WriteByte(checked((byte)arguments.Length));
            foreach (var argument in arguments)
                argument.Write(writer);
        });
        return (result, reader.ReadRemaining());
    }
    public static void ReadResults(RemoteArgument[] arguments, byte[] results) {
        var reader = new ByteReader(results);
        foreach (var argument in arguments)
            argument.Read(reader);
    }
    // Which of the interfaces each object behind the handles has: one flag per handle and interface, in that order
    public async Task<bool[]> QueryAsync(uint[] handles, Guid[] iids) {
        var (result, reader) = await RequestAsync(RemoteCommand.Query, writer => {
            writer.WriteUInt32((uint)handles.Length);
            writer.WriteByte(checked((byte)iids.Length));
            foreach (var handle in handles)
                writer.WriteUInt32(handle);
            foreach (var iid in iids)
                writer.WriteGuid(iid);
        });
        ThrowIfFailed(result, RemoteCommand.Query);
        var flags = new bool[handles.Length * iids.Length];
        for (var i = 0; i < flags.Length; i++)
            flags[i] = reader.ReadByte() != 0;
        return flags;
    }
    // Gives 'count' hand-outs of a handle back to the agent. The proxies are collected in bursts, so the pairs are
    // gathered for a moment and go out in one request; nothing waits for the answer
    public void Release(uint handle, uint count) {
        lock (pendingReleases) {
            pendingReleases.Add(handle);
            pendingReleases.Add(count);
            if (releaseScheduled)
                return;
            releaseScheduled = true;
        }
        _ = Task.Run(FlushReleasesAsync);
    }
    // Ends the debuggee; the agent answers before it goes, and the connection drops right after
    public async Task TerminateAsync(int exitCode = 0) {
        var (result, _) = await RequestAsync(RemoteCommand.Terminate, writer => writer.WriteUInt32((uint)exitCode));
        ThrowIfFailed(result, RemoteCommand.Terminate);
    }

    public void Dispose() {
        lost = true;
        client.Dispose();
        FailPending(null);
    }

    private async Task FlushReleasesAsync() {
        await Task.Delay(ReleaseDelayMilliseconds);
        uint[] pairs;
        lock (pendingReleases) {
            pairs = pendingReleases.ToArray();
            pendingReleases.Clear();
            releaseScheduled = false;
        }
        try {
            await RequestAsync(RemoteCommand.Release, writer => {
                foreach (var value in pairs)
                    writer.WriteUInt32(value);
            });
        }
        catch (Exception) {
            // A lost connection released everything already
        }
    }
    // Sends the request and returns its HRESULT with a reader positioned after it
    private async Task<(int, ByteReader)> RequestAsync(RemoteCommand command, Action<ByteWriter>? writeArguments = null) {
        if (stream == null)
            throw new InvalidOperationException("The client is not connected");
        if (lost)
            throw new IOException("The agent connection was lost");
        var sequence = Interlocked.Increment(ref nextSequence);
        var completion = new TaskCompletionSource<ByteReader>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequests[sequence] = completion;
        // The connection may have gone between the check and the registration
        if (lost)
            FailPending(null);

        var body = new ByteWriter();
        body.WriteByte((byte)RemoteFrameKind.Request);
        body.WriteUInt32(sequence);
        body.WriteUInt16((ushort)command);
        writeArguments?.Invoke(body);
        var bytes = body.ToArray();
        var frame = new byte[4 + bytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)bytes.Length);
        bytes.CopyTo(frame, 4);

        await writeLock.WaitAsync();
        try {
            await stream.WriteAsync(frame);
        }
        finally {
            writeLock.Release();
        }

        var reader = await completion.Task;
        return (reader.ReadInt32(), reader);
    }
    private static void ThrowIfFailed(int result, RemoteCommand command) {
        if (result < 0)
            throw new COMException($"The agent failed the '{command}' request", result);
    }
    private async Task ReadFramesAsync() {
        try {
            while (true) {
                var header = new byte[4];
                await ReadExactlyAsync(header);
                var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
                var frame = new byte[length];
                await ReadExactlyAsync(frame);

                var reader = new ByteReader(frame);
                var kind = (RemoteFrameKind)reader.ReadByte();
                var sequence = reader.ReadUInt32();
                if (kind == RemoteFrameKind.Response) {
                    if (pendingRequests.TryRemove(sequence, out var completion))
                        completion.TrySetResult(reader);
                }
                else if (kind == RemoteFrameKind.Event) {
                    var remoteEvent = (RemoteEvent)reader.ReadUInt16();
                    if (remoteEvent == RemoteEvent.Callback)
                        callbacks.Writer.TryWrite(new RemoteCallbackEvent((RemoteCallback)reader.ReadUInt16(), reader));
                }
            }
        }
        catch (Exception ex) {
            lost = true;
            FailPending(ex);
            callbacks.Writer.TryComplete();
            Disconnected?.Invoke();
        }
    }
    private void FailPending(Exception? cause) {
        foreach (var pending in pendingRequests.Values)
            pending.TrySetException(new IOException("The agent connection was lost", cause));
        pendingRequests.Clear();
    }
    private void EnsureDispatching() {
        lock (callbacks) {
            if (dispatching)
                return;
            dispatching = true;
        }
        _ = Task.Run(DispatchCallbacksAsync);
    }
    private async Task DispatchCallbacksAsync() {
        await foreach (var callbackEvent in callbacks.Reader.ReadAllAsync())
            callbackReceived?.Invoke(callbackEvent);
    }
    private async Task ReadExactlyAsync(byte[] buffer) {
        var offset = 0;
        while (offset < buffer.Length) {
            var read = await stream!.ReadAsync(buffer.AsMemory(offset));
            if (read <= 0)
                throw new EndOfStreamException("The agent closed the connection");
            offset += read;
        }
    }
}
