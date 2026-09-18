using DotNet.Debugging.CorApi;
using DotNet.Debugging.Remote.Protocol;

namespace DotNet.Debugging.Remote.Proxy;

// The host's view of one agent connection: the proxies of the objects the agent handed out, one per handle so the
// debugger sees the same object every time, and the return of the handles the debugger let go of. A value or a frame
// comes in several kinds that the debugger tells apart by QueryInterface; the request that brings such an object asks
// which of the kind-telling interfaces it has, and the proxy class exposes exactly those. An object that arrives
// without that answer (the argument of a callback) is asked about in a Query first
public partial class RemoteSession {
    private static readonly Guid[] valueProbe = [Iids.GenericValue, Iids.ReferenceValue, Iids.HandleValue, Iids.HeapValue, Iids.ObjectValue, Iids.StringValue, Iids.ArrayValue, Iids.BoxValue, Iids.ExceptionObjectValue, Iids.DelegateObjectValue];
    private static readonly Guid[] frameProbe = [Iids.ILFrame, Iids.NativeFrame, Iids.InternalFrame, Iids.RuntimeUnwindableFrame];
    private static readonly HashSet<Type> valueInterfaces = [
        typeof(ICorDebugValue), typeof(ICorDebugValue2), typeof(ICorDebugValue3), typeof(ICorDebugGenericValue), typeof(ICorDebugReferenceValue),
        typeof(ICorDebugHandleValue), typeof(ICorDebugHeapValue), typeof(ICorDebugHeapValue2), typeof(ICorDebugHeapValue3), typeof(ICorDebugHeapValue4),
        typeof(ICorDebugObjectValue), typeof(ICorDebugObjectValue2), typeof(ICorDebugStringValue), typeof(ICorDebugArrayValue), typeof(ICorDebugBoxValue),
        typeof(ICorDebugExceptionObjectValue), typeof(ICorDebugDelegateObjectValue),
    ];
    private static readonly HashSet<Type> frameInterfaces = [
        typeof(ICorDebugFrame), typeof(ICorDebugILFrame), typeof(ICorDebugILFrame2), typeof(ICorDebugILFrame3), typeof(ICorDebugILFrame4),
        typeof(ICorDebugNativeFrame), typeof(ICorDebugNativeFrame2), typeof(ICorDebugInternalFrame), typeof(ICorDebugInternalFrame2), typeof(ICorDebugRuntimeUnwindableFrame),
    ];

    private readonly Dictionary<uint, WeakReference<RemoteObject>> proxies;
    // The proxies of what lives as long as the process does (modules, functions, classes, types, importers): kept, so
    // the answers they remember are not lost each time the debugger lets go of them between two stops
    private readonly List<RemoteObject> longLived;
    private readonly Action detach;
    // The debugger's Continue calls held back during a replay of the attach, one per replayed callback
    private readonly SemaphoreSlim heldContinues;
    private int replaying;

    public RemoteDebuggerClient Client { get; }
    // Where the build put copies of the app's assemblies, for a module path that only exists on the device: the
    // launch's assemblies path is a ';' separated list of folders (the app's assets and the host library's folder)
    public IReadOnlyList<string> AssembliesPaths { get; }
    // The attach is being replayed to the debugger from the process (RemoteCorDebug): the process is held stopped
    // through the whole sequence, and the debugger's Continue after each callback is answered without being made
    public bool ReplayingAttach => replaying != 0;

    public RemoteSession(RemoteDebuggerClient client, string? assembliesPath, Action detach) {
        Client = client;
        AssembliesPaths = assembliesPath == null ? [] : assembliesPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        this.detach = detach;
        proxies = new Dictionary<uint, WeakReference<RemoteObject>>();
        longLived = new List<RemoteObject>();
        heldContinues = new SemaphoreSlim(0);
    }

    // The interfaces that tell the kind of an object of the given type apart, null when the type has one proxy class
    public static Guid[]? ProbeFor(Type type) {
        if (valueInterfaces.Contains(type))
            return valueProbe;
        if (frameInterfaces.Contains(type))
            return frameProbe;
        return null;
    }
    // The proxy of a handle the agent just handed out: the one the debugger already holds for it, or a new one; a
    // handle nothing can proxy is given back at once. 'flags' answer the type's probe when the request carried it
    public T? GetProxy<T>(uint handle, bool[]? flags = null) where T : class {
        return GetProxies<T>([handle], flags == null ? null : [flags])[0];
    }
    // The proxies of several handles at once; the kinds of the new ones without an answer are asked for in a single
    // round trip
    public T?[] GetProxies<T>(uint[] handles, bool[][]? flags = null) where T : class {
        var result = new T?[handles.Length];
        var pending = new List<int>();
        lock (proxies) {
            for (var i = 0; i < handles.Length; i++) {
                if (handles[i] == 0)
                    continue;
                if (TryGetExisting(handles[i], out T? existing))
                    result[i] = existing;
                else
                    pending.Add(i);
            }
        }
        if (pending.Count == 0)
            return result;

        var probe = ProbeFor(typeof(T));
        var kinds = new bool[pending.Count][];
        var unknown = new List<int>();
        for (var k = 0; k < pending.Count; k++) {
            var known = flags != null && pending[k] < flags.Length ? flags[pending[k]] : null;
            if (probe == null)
                kinds[k] = [];
            else if (known != null && known.Length == probe.Length)
                kinds[k] = known;
            else
                unknown.Add(k);
        }
        if (unknown.Count > 0 && probe != null) {
            var asked = new uint[unknown.Count];
            for (var i = 0; i < asked.Length; i++)
                asked[i] = handles[pending[unknown[i]]];
            var answers = Query(asked, probe);
            for (var i = 0; i < unknown.Count; i++)
                kinds[unknown[i]] = answers == null ? [] : answers.AsSpan(i * probe.Length, probe.Length).ToArray();
        }
        lock (proxies) {
            for (var k = 0; k < pending.Count; k++) {
                var i = pending[k];
                var handle = handles[i];
                // Made meanwhile by another thread
                if (TryGetExisting(handle, out T? existing)) {
                    result[i] = existing;
                    continue;
                }
                var created = CreateProxy<T>(handle, kinds[k].Length == 0 ? null : kinds[k]);
                if (created == null) {
                    HostLog.Write($"no proxy for {typeof(T).Name}, handle {handle} given back");
                    Client.Release(handle, 1);
                    continue;
                }
                proxies[handle] = new WeakReference<RemoteObject>(created);
                if (IsLongLived(created))
                    longLived.Add(created);
                result[i] = (T)(object)created;
            }
        }
        return result;
    }
    // A hand-out of a handle that is not going to be used
    public void Discard(uint handle) {
        if (handle != 0)
            Client.Release(handle, 1);
    }
    // Lets the controller behind 'handle' run, for a callback the debugger is not handed
    public int Continue(uint handle) {
        try {
            var (hr, _) = Client.InvokeAsync(handle, Iids.Controller, 4, [RemoteArgument.Bool(false)]).GetAwaiter().GetResult();
            return hr;
        }
        catch (Exception ex) {
            HostLog.Write($"continuing handle {handle} failed: {ex.Message}");
            return Cor.CORDBG_E_PROCESS_TERMINATED;
        }
    }
    public void Detach() {
        detach();
    }
    public void BeginReplay() {
        Interlocked.Exchange(ref replaying, 1);
    }
    public void EndReplay() {
        Interlocked.Exchange(ref replaying, 0);
    }
    // A controller's Continue during the replay: answered here, and counted for the replay to go on with the next
    // callback; false outside a replay, when the call goes to the agent
    public bool TryHoldContinue() {
        if (!ReplayingAttach)
            return false;
        heldContinues.Release();
        return true;
    }
    // Waits for the debugger to be done with the replayed callback it was handed, which it shows by continuing
    public bool WaitForHeldContinue(TimeSpan timeout) {
        return heldContinues.Wait(timeout);
    }

    internal void Release(uint handle, uint count) {
        lock (proxies) {
            if (proxies.TryGetValue(handle, out var reference) && !reference.TryGetTarget(out _))
                proxies.Remove(handle);
        }
        Client.Release(handle, count);
    }

    private static bool IsLongLived(RemoteObject proxy) {
        return proxy is RemoteCorDebugAppDomain or RemoteCorDebugAssembly or RemoteCorDebugModule or RemoteMetaDataImport
            or RemoteCorDebugFunction or RemoteCorDebugClass or RemoteCorDebugType or RemoteCorDebugCode;
    }
    private bool TryGetExisting<T>(uint handle, out T? existing) where T : class {
        existing = null;
        if (!proxies.TryGetValue(handle, out var reference) || !reference.TryGetTarget(out var known) || known is not T typed)
            return false;
        known.AddIssued();
        existing = typed;
        return true;
    }
    private bool[]? Query(uint[] handles, Guid[] probe) {
        try {
            return Client.QueryAsync(handles, probe).GetAwaiter().GetResult();
        }
        catch (Exception ex) {
            HostLog.Write($"asking the kinds of {handles.Length} objects failed: {ex.Message}");
            return null;
        }
    }
    private RemoteObject? CreateProxy<T>(uint handle, bool[]? flags) {
        var type = typeof(T);
        if (valueInterfaces.Contains(type))
            return CreateValueProxy(handle, flags);
        if (frameInterfaces.Contains(type))
            return CreateFrameProxy(handle, flags);
        return CreateUniqueProxy<T>(handle);
    }
    // The kinds the runtime's debugging library makes, in the order of 'valueProbe': generic, reference, handle,
    // heap, object, string, array, box, exception object, delegate object
    private RemoteObject CreateValueProxy(uint handle, bool[]? flags) {
        if (flags == null)
            return new RemoteCorDebugGenericValue(this, handle);
        if (flags[5])
            return new RemoteCorDebugStringValue(this, handle);
        if (flags[6])
            return new RemoteCorDebugArrayValue(this, handle);
        if (flags[7])
            return new RemoteCorDebugBoxValue(this, handle);
        if (flags[8])
            return new RemoteCorDebugExceptionObjectValue(this, handle);
        if (flags[9])
            return new RemoteCorDebugDelegateObjectValue(this, handle);
        if (flags[4] && flags[3])
            return new RemoteCorDebugObjectValue(this, handle);
        if (flags[4])
            return new RemoteCorDebugStructValue(this, handle);
        if (flags[0])
            return new RemoteCorDebugGenericValue(this, handle);
        if (flags[2])
            return new RemoteCorDebugHandleValue(this, handle);
        if (flags[1])
            return new RemoteCorDebugReferenceValue(this, handle);
        HostLog.Write($"a value of no known kind, handle {handle}: {string.Join(",", flags)}");
        return new RemoteCorDebugGenericValue(this, handle);
    }
    // In the order of 'frameProbe': IL, native, internal, runtime-unwindable
    private RemoteObject CreateFrameProxy(uint handle, bool[]? flags) {
        if (flags == null)
            return new RemoteCorDebugFrame(this, handle);
        if (flags[0] && flags[1])
            return new RemoteCorDebugManagedFrame(this, handle);
        if (flags[0])
            return new RemoteCorDebugILFrame(this, handle);
        if (flags[1])
            return new RemoteCorDebugNativeFrame(this, handle);
        if (flags[2])
            return new RemoteCorDebugInternalFrame(this, handle);
        if (flags[3])
            return new RemoteCorDebugRuntimeUnwindableFrame(this, handle);
        return new RemoteCorDebugFrame(this, handle);
    }
}
