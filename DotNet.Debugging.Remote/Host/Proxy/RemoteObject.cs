using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Remote.Protocol;

namespace DotNet.Debugging.Remote.Proxy;

// An ICorDebug object living in the app, reached through its handle. A proxy answers each method of the interfaces it
// implements with one request naming the interface, the vtable slot and the arguments; the generator writes those
// methods from the CorApi definitions, a method the wire has no kinds for is refused with E_NOTIMPL and logged. The
// hand-outs of the handle are given back when the proxy is collected, which is when the debugger let go of it.
// A name is asked for twice, once for its length and once with a buffer of that length; the first request goes out
// with a buffer of its own, so the name comes along, and the second is answered from it without a round trip
public abstract class RemoteObject {
    // The buffer a length query is widened to, in characters; a longer name takes the second round trip
    private const uint NameProbeCapacity = 1024;
    private const int NameCacheLimit = 4096;

    private int issued;
    // Memory handed to the debugger for bytes a callee pointed into its own (metadata signatures, constants)
    private List<nint>? blobs;
    // The complete responses of the calls that returned a name, by call
    private Dictionary<string, CachedResponse>? names;

    private sealed class CachedResponse {
        public int HResult;
        public byte[] Results = [];
        public uint Capacity;
    }

    public RemoteSession Session { get; }
    public uint Handle { get; }

    protected RemoteObject(RemoteSession session, uint handle) {
        Session = session;
        Handle = handle;
        issued = 1;
    }
    ~RemoteObject() {
        Session.Release(Handle, (uint)issued);
        if (blobs != null) {
            foreach (var blob in blobs)
                Marshal.FreeHGlobal(blob);
        }
    }

    // The handle of an object the debugger passes back in, zero for anything that is not one of ours
    public static uint HandleOf(object? instance) {
        return instance is RemoteObject remote ? remote.Handle : 0;
    }
    public static uint[] HandlesOf(object[]? instances) {
        if (instances == null)
            return [];
        var handles = new uint[instances.Length];
        for (var i = 0; i < handles.Length; i++)
            handles[i] = HandleOf(instances[i]);
        return handles;
    }
    internal void AddIssued() {
        Interlocked.Increment(ref issued);
    }

    protected int Invoke(Guid iid, ushort slot, params RemoteArgument[] arguments) {
        try {
            var hasText = false;
            foreach (var argument in arguments)
                hasText |= argument.IsText;
            if (!hasText)
                return InvokeRemote(iid, slot, arguments);
            var key = KeyOf(iid, slot, arguments);
            if (TryAnswerFromCache(key, arguments, out var cached))
                return cached;
            foreach (var argument in arguments)
                argument.Widen(NameProbeCapacity);
            var hr = InvokeRemote(iid, slot, arguments, out var results);
            Remember(key, hr, results, arguments);
            return hr;
        }
        catch (Exception ex) {
            return ToHresult(ex, $"slot {slot}");
        }
    }
    protected int InvokeUInt32(Guid iid, ushort slot, out uint value, params RemoteArgument[] arguments) {
        var result = RemoteArgument.OutUInt32();
        var hr = Invoke(iid, slot, Append(arguments, result));
        value = result.UInt32Result;
        return hr;
    }
    protected int InvokeUInt64(Guid iid, ushort slot, out ulong value, params RemoteArgument[] arguments) {
        var result = RemoteArgument.OutUInt64();
        var hr = Invoke(iid, slot, Append(arguments, result));
        value = result.UInt64Result;
        return hr;
    }
    protected int InvokeBool(Guid iid, ushort slot, out bool value, params RemoteArgument[] arguments) {
        var hr = InvokeUInt32(iid, slot, out var number, arguments);
        value = number != 0;
        return hr;
    }
    protected int InvokeObject<T>(Guid iid, ushort slot, out T value, params RemoteArgument[] arguments) where T : class {
        var result = RemoteArgument.OutObject(RemoteSession.ProbeFor(typeof(T)));
        var hr = Invoke(iid, slot, Append(arguments, result));
        value = Session.GetProxy<T>(result.HandleResult, result.FlagsResult)!;
        return hr;
    }
    // The names' pattern, forwarded as asked: a call without a buffer reports the length, terminator included
    protected int InvokeText(Guid iid, ushort slot, uint capacity, out uint length, char[]? buffer) {
        var result = RemoteArgument.OutText(capacity);
        var hr = Invoke(iid, slot, result);
        length = result.LengthResult;
        CopyText(result.TextResult, buffer);
        return hr;
    }
    // A request the debugger waits for: the agent's answer decides the HRESULT, a lost connection reads as a dead process
    protected int Forward(Func<Task> request, string name) {
        try {
            request().GetAwaiter().GetResult();
            return Cor.S_OK;
        }
        catch (Exception ex) {
            return ToHresult(ex, name);
        }
    }
    protected int NotProxied(string method) {
        HostLog.Write($"{GetType().Name}.{method} is not proxied");
        return Cor.E_NOTIMPL;
    }
    // The proxies of the handles an out array came back with, in place
    protected void FillProxies<T>(T[]? target, uint[] handles, bool[][] flags) where T : class {
        if (target == null)
            return;
        var proxies = Session.GetProxies<T>(handles, flags);
        for (var i = 0; i < target.Length && i < proxies.Length; i++)
            target[i] = proxies[i]!;
    }
    // A name the debugger asked for with a buffer of its own: what fits is copied, NUL-terminated when there is room
    protected static void CopyText(string text, char[]? buffer) {
        if (buffer == null)
            return;
        var copied = Math.Min(buffer.Length, text.Length);
        text.CopyTo(0, buffer, 0, copied);
        if (copied < buffer.Length)
            buffer[copied] = '\0';
    }
    // The bytes a callee pointed at, in memory the debugger may read for as long as this proxy lives, the way the
    // callee keeps its own
    protected nint KeepBlob(byte[] bytes) {
        if (bytes.Length == 0)
            return 0;
        var blob = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, blob, bytes.Length);
        lock (this) {
            blobs ??= new List<nint>();
            blobs.Add(blob);
        }
        return blob;
    }
    // Bytes the debugger points at in its own memory, which is this process
    protected static byte[] BytesAt(nint pointer, uint count) {
        if (pointer == 0 || count == 0)
            return [];
        var bytes = new byte[count];
        Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return bytes;
    }
    protected static void CopyBytes(byte[] source, byte[]? target) {
        if (target != null)
            Array.Copy(source, target, Math.Min(source.Length, target.Length));
    }
    // Arrays of plain values travel as their bytes
    protected static byte[] BytesOf<T>(T[]? values) where T : unmanaged {
        return values == null ? [] : MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }
    protected static void CopyValues<T>(byte[] source, T[]? target) where T : unmanaged {
        if (target == null)
            return;
        var values = MemoryMarshal.Cast<byte, T>(source);
        values.Slice(0, Math.Min(values.Length, target.Length)).CopyTo(target);
    }
    protected static T ReadValue<T>(byte[] source) where T : unmanaged {
        return source.Length >= Unsafe.SizeOf<T>() ? MemoryMarshal.Read<T>(source) : default;
    }

    private int InvokeRemote(Guid iid, ushort slot, RemoteArgument[] arguments) {
        return InvokeRemote(iid, slot, arguments, out _);
    }
    private int InvokeRemote(Guid iid, ushort slot, RemoteArgument[] arguments, out byte[] results) {
        int hr;
        (hr, results) = Session.Client.InvokeAsync(Handle, iid, slot, arguments).GetAwaiter().GetResult();
        RemoteDebuggerClient.ReadResults(arguments, results);
        HostLog.Write($"{GetType().Name}[{Handle}] slot {slot} ({string.Join(", ", arguments.Select(a => a.ToString()))}) -> 0x{hr:X8}");
        return hr;
    }
    // The call without the sizes of its text buffers: the same call asked with a different buffer
    private static string KeyOf(Guid iid, ushort slot, RemoteArgument[] arguments) {
        var writer = new ByteWriter();
        writer.WriteGuid(iid);
        writer.WriteUInt16(slot);
        foreach (var argument in arguments)
            argument.WriteKey(writer);
        return Convert.ToHexString(writer.ToArray());
    }
    // A cached response serves a call whose buffers are large enough for the names it holds, or that asks their length
    private bool TryAnswerFromCache(string key, RemoteArgument[] arguments, out int hr) {
        hr = 0;
        CachedResponse? cached;
        lock (this) {
            if (names == null || !names.TryGetValue(key, out cached))
                return false;
        }
        RemoteDebuggerClient.ReadResults(arguments, cached.Results);
        foreach (var argument in arguments) {
            if (argument.IsText && argument.Capacity != 0 && argument.Capacity < argument.LengthResult)
                return false;
        }
        hr = cached.HResult;
        return true;
    }
    // Only a response with every name complete is kept: a longer one is asked for again with the right buffer
    private void Remember(string key, int hr, byte[] results, RemoteArgument[] arguments) {
        foreach (var argument in arguments) {
            if (argument.IsText && argument.LengthResult > argument.Capacity)
                return;
        }
        lock (this) {
            names ??= new Dictionary<string, CachedResponse>();
            if (names.Count >= NameCacheLimit)
                names.Clear();
            names[key] = new CachedResponse { HResult = hr, Results = results, Capacity = NameProbeCapacity };
        }
    }
    private int ToHresult(Exception ex, string name) {
        HostLog.Write($"{GetType().Name} {name} failed: {ex.Message}");
        if (ex is COMException com)
            return com.HResult;
        return Cor.CORDBG_E_PROCESS_TERMINATED;
    }
    private static RemoteArgument[] Append(RemoteArgument[] arguments, RemoteArgument last) {
        var all = new RemoteArgument[arguments.Length + 1];
        arguments.CopyTo(all, 0);
        all[arguments.Length] = last;
        return all;
    }
}
