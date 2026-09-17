using System.Net;
using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Remote.Protocol;
using DotNet.Debugging.Remote.Proxy;

namespace DotNet.Debugging.Remote;

// The ICorDebug a debugger gets from this library: it reaches the agent in the app (listening for it when the debugger
// is the server, connecting out otherwise) and hands the agent's callbacks to the debugger's managed callback with the
// objects proxied, the way a runtime would. A callback the debugger has no use for is continued here, unseen by it.
// An agent whose attach callbacks went to an earlier host, or to none, says so in Hello: the same sequence is then
// rebuilt from the process (ReplayAttach)
[GeneratedComClass]
public partial class RemoteCorDebug : ICorDebug {
    // How long a replayed callback waits for the debugger to continue before the next one goes out
    private static readonly TimeSpan ReplayStepTimeout = TimeSpan.FromSeconds(10);

    private readonly string address;
    private readonly int port;
    private readonly bool isServer;
    private readonly string? platform;
    private readonly string? assembliesPath;
    private readonly object stateLock = new object();
    private RemoteAgentListener? listener;
    private RemoteDebuggerClient? client;
    private RemoteSession? session;
    private ICorDebugManagedCallback? managedCallback;
    private ICorDebugProcess? process;
    private bool exitReported;

    public RemoteCorDebug(string address, int port, bool isServer, string? platform, string? assembliesPath) {
        this.address = address;
        this.port = port;
        this.isServer = isServer;
        this.platform = platform;
        this.assembliesPath = assembliesPath;
        if (isServer) {
            // Listening before the launch returns: the app connects out right after it starts
            listener = new RemoteAgentListener(IPAddress.Parse(address), port);
            listener.Start();
            _ = Task.Run(WaitForAgentAsync);
        }
        else {
            _ = Task.Run(ConnectToAgentAsync);
        }
    }

    public override string ToString() {
        return $"{address}:{port} {(isServer ? "listening" : "connecting")}, platform '{platform}', assemblies '{assembliesPath}'";
    }

    public int TryInitialize() {
        return Cor.S_OK;
    }
    public int TryTerminate() {
        HostLog.Write("ICorDebug.Terminate");
        Close();
        return Cor.S_OK;
    }
    public int TrySetManagedHandler(ICorDebugManagedCallback pCallback) {
        lock (stateLock)
            managedCallback = pCallback;
        HostLog.Write("ICorDebug.SetManagedHandler");
        return Cor.S_OK;
    }
    public int TrySetUnmanagedHandler(ICorDebugUnmanagedCallback pCallback) {
        return Cor.S_OK;
    }
    public int TryCreateProcess(string lpApplicationName, string lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, nint lpEnvironment, string lpCurrentDirectory, nint lpStartupInfo, nint lpProcessInformation, CorDebugCreateProcessFlags debuggingFlags, out ICorDebugProcess ppProcess) {
        ppProcess = null!;
        return Cor.E_NOTIMPL;
    }
    // A remote target has no process to attach to from here: the process arrives through the CreateProcess callback
    // once the agent in the app reports it, which is what the debugger expects of a remote attach
    public int TryDebugActiveProcess(uint id, bool win32Attach, out ICorDebugProcess ppProcess) {
        HostLog.Write($"ICorDebug.DebugActiveProcess({id}): the process is reported through CreateProcess");
        ppProcess = null!;
        return Cor.E_NOTIMPL;
    }
    public int TryEnumerateProcesses(out ICorDebugProcessEnum ppProcess) {
        ppProcess = null!;
        return Cor.E_NOTIMPL;
    }
    public int TryGetProcess(uint dwProcessId, out ICorDebugProcess ppProcess) {
        ppProcess = null!;
        return Cor.E_NOTIMPL;
    }
    public int TryCanLaunchOrAttach(uint dwProcessId, bool win32DebuggingEnabled) {
        return Cor.S_OK;
    }

    // The debugger let go of the process: the agent continues whatever it held stopped once the connection drops
    internal void Detach() {
        HostLog.Write("detaching from the agent");
        Close();
    }

    private async Task WaitForAgentAsync() {
        try {
            var connected = await listener!.AcceptAsync();
            listener.Dispose();
            var info = await connected.HelloAsync();
            AttachAgent(connected, info);
        }
        catch (Exception ex) {
            HostLog.Write($"waiting for the agent failed: {ex.Message}");
        }
    }
    // The app may take a while to start; its agent is tried for until it answers. A port forwarded by adb or a device
    // tunnel accepts the connection before anything listens behind it and drops it right after, so only an answered
    // Hello counts as the agent; the callbacks it sends meanwhile wait in the client until the handler is subscribed
    private async Task ConnectToAgentAsync() {
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (true) {
            var connecting = new RemoteDebuggerClient();
            try {
                await connecting.ConnectAsync(address, port);
                var info = await connecting.HelloAsync().WaitAsync(TimeSpan.FromSeconds(5));
                AttachAgent(connecting, info);
                return;
            }
            catch (Exception ex) {
                connecting.Dispose();
                if (DateTime.UtcNow >= deadline) {
                    HostLog.Write($"the agent at {address}:{port} never answered: {ex.Message}");
                    return;
                }
                await Task.Delay(250);
            }
        }
    }
    // The agent attaches to its runtime as soon as the connection stands and reports CreateProcess right away; a
    // replay of an attach that already happened comes before the callbacks the agent queued meanwhile
    private void AttachAgent(RemoteDebuggerClient connected, RemoteAgentInfo info) {
        var created = new RemoteSession(connected, assembliesPath, Detach);
        lock (stateLock) {
            client = connected;
            session = created;
        }
        HostLog.Write($"agent connected: protocol {info.ProtocolVersion}, pid {info.ProcessId}, runtime {info.RuntimeDirectory}");
        if (info.ProtocolVersion != RemoteAgent.ProtocolVersion)
            HostLog.Write($"the agent speaks protocol {info.ProtocolVersion}, this host protocol {RemoteAgent.ProtocolVersion}");
        connected.Disconnected += OnDisconnected;
        if (info.ProcessHandle != 0)
            ReplayAttach(created, info.ProcessHandle);
        connected.CallbackReceived += OnCallback;
    }
    // The attach callbacks of a runtime: CreateProcess, then the app domain, its assemblies with their modules, and
    // the threads. The process is stopped for the whole sequence; the debugger's Continue after each callback is held
    // by the session, and its arrival lets the next callback out, so the debugger sees the sequence as it would from
    // the runtime
    private void ReplayAttach(RemoteSession current, uint processHandle) {
        ICorDebugManagedCallback? callback;
        lock (stateLock)
            callback = managedCallback;
        var replayed = current.GetProxy<ICorDebugProcess>(processHandle);
        if (callback == null || replayed == null) {
            HostLog.Write("the attach cannot be replayed: no managed handler or no process");
            return;
        }
        lock (stateLock)
            process = replayed;
        HostLog.Write("replaying the attach from the process");
        var stopped = replayed.TryStop(0);
        if (stopped != Cor.S_OK)
            HostLog.Write($"stopping the process for the replay failed: 0x{stopped:X8}");
        current.BeginReplay();
        try {
            Replay(current, () => callback.TryCreateProcess(replayed), "CreateProcess");
            if (replayed.TryEnumerateAppDomains(out var domains) == Cor.S_OK) {
                foreach (var domain in Drain(domains, 8, (ICorDebugAppDomainEnum e, uint c, ICorDebugAppDomain[] a, out uint f) => e.TryNext(c, a, out f)))
                    ReplayDomain(current, callback, replayed, domain);
            }
            if (replayed.TryEnumerateThreads(out var threads) == Cor.S_OK) {
                foreach (var thread in Drain(threads, 16, (ICorDebugThreadEnum e, uint c, ICorDebugThread[] a, out uint f) => e.TryNext(c, a, out f))) {
                    if (thread.TryGetAppDomain(out var domain) == Cor.S_OK)
                        Replay(current, () => callback.TryCreateThread(domain, thread), "CreateThread");
                }
            }
        }
        catch (Exception ex) {
            HostLog.Write($"the replay of the attach failed: {ex.Message}");
        }
        finally {
            current.EndReplay();
            if (stopped == Cor.S_OK)
                replayed.TryContinue(false);
        }
        HostLog.Write("the attach is replayed");
    }
    private void ReplayDomain(RemoteSession current, ICorDebugManagedCallback callback, ICorDebugProcess replayed, ICorDebugAppDomain domain) {
        Replay(current, () => callback.TryCreateAppDomain(replayed, domain), "CreateAppDomain");
        if (domain.TryEnumerateAssemblies(out var assemblies) != Cor.S_OK)
            return;
        foreach (var assembly in Drain(assemblies, 64, (ICorDebugAssemblyEnum e, uint c, ICorDebugAssembly[] a, out uint f) => e.TryNext(c, a, out f))) {
            Replay(current, () => callback.TryLoadAssembly(domain, assembly), "LoadAssembly");
            if (assembly.TryEnumerateModules(out var modules) != Cor.S_OK)
                continue;
            foreach (var module in Drain(modules, 8, (ICorDebugModuleEnum e, uint c, ICorDebugModule[] a, out uint f) => e.TryNext(c, a, out f)))
                Replay(current, () => callback.TryLoadModule(domain, module), "LoadModule");
        }
    }
    // One replayed callback: handed to the debugger, then waited for until the debugger continues
    private static void Replay(RemoteSession current, Func<int> deliver, string name) {
        var result = deliver();
        if (result != Cor.S_OK) {
            HostLog.Write($"the debugger failed the replayed {name}: 0x{result:X8}");
            return;
        }
        if (!current.WaitForHeldContinue(ReplayStepTimeout))
            HostLog.Write($"the debugger did not continue after the replayed {name}");
    }
    private delegate int NextFunc<TEnum, TItem>(TEnum enumerator, uint count, TItem[] items, out uint fetched);
    // Everything an enumerator has; a page shorter than asked comes back with S_FALSE, which is not a failure
    private static List<TItem> Drain<TEnum, TItem>(TEnum enumerator, uint batch, NextFunc<TEnum, TItem> next) where TItem : class {
        var items = new List<TItem>();
        while (true) {
            var page = new TItem[batch];
            if (next(enumerator, batch, page, out var fetched) < 0 || fetched == 0)
                return items;
            for (var i = 0; i < fetched && i < page.Length; i++) {
                if (page[i] != null)
                    items.Add(page[i]);
            }
            if (fetched < batch)
                return items;
        }
    }

    private void OnCallback(RemoteCallbackEvent callbackEvent) {
        ICorDebugManagedCallback? callback;
        RemoteSession? current;
        lock (stateLock) {
            callback = managedCallback;
            current = session;
        }
        if (current == null)
            return;
        var arguments = new RemoteCallbackArguments(current, callbackEvent.Data);
        if (callbackEvent.Id == RemoteCallback.ExitProcess) {
            arguments.DiscardRemaining(1);
            ReportExit(callback);
            return;
        }
        int? result = null;
        try {
            result = callback == null ? null : Deliver(callback, callbackEvent.Id, arguments);
        }
        catch (Exception ex) {
            HostLog.Write($"delivering {callbackEvent.Id} failed: {ex.Message}");
        }
        if (result == null) {
            // Not handed to the debugger: the stop is released here, the debugger sees nothing of it
            HostLog.Write($"continuing '{callbackEvent.Id}', not delivered");
            var controller = arguments.ControllerHandle != 0 ? arguments.ControllerHandle : RemoteObject.HandleOf(process);
            if (controller != 0)
                current.Continue(controller);
            arguments.DiscardRemaining(HandleCount(callbackEvent.Id));
            return;
        }
        if (result != Cor.S_OK)
            HostLog.Write($"the debugger failed {callbackEvent.Id}: 0x{result:X8}");
        // The debugger continues the process when it is done with the callback, as it would with a runtime
    }
    // Hands the callback to the debugger with its arguments proxied; null for one the debugger cannot take yet
    private int? Deliver(ICorDebugManagedCallback callback, RemoteCallback id, RemoteCallbackArguments arguments) {
        switch (id) {
            case RemoteCallback.CreateProcess: {
                var created = arguments.Proxy<ICorDebugProcess>()!;
                lock (stateLock)
                    process = created;
                HostLog.Write("reporting CreateProcess to the debugger");
                return callback.TryCreateProcess(created);
            }
            case RemoteCallback.CreateAppDomain:
                return callback.TryCreateAppDomain(arguments.Proxy<ICorDebugProcess>()!, arguments.Proxy<ICorDebugAppDomain>()!);
            case RemoteCallback.ExitAppDomain:
                return callback.TryExitAppDomain(arguments.Proxy<ICorDebugProcess>()!, arguments.Proxy<ICorDebugAppDomain>()!);
            case RemoteCallback.LoadAssembly:
                return callback.TryLoadAssembly(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugAssembly>()!);
            case RemoteCallback.UnloadAssembly:
                return callback.TryUnloadAssembly(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugAssembly>()!);
            case RemoteCallback.LoadModule:
                return callback.TryLoadModule(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugModule>()!);
            case RemoteCallback.UnloadModule:
                return callback.TryUnloadModule(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugModule>()!);
            case RemoteCallback.LoadClass:
                return callback.TryLoadClass(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugClass>()!);
            case RemoteCallback.UnloadClass:
                return callback.TryUnloadClass(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugClass>()!);
            case RemoteCallback.CreateThread:
                return callback.TryCreateThread(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugThread>()!);
            case RemoteCallback.ExitThread:
                return callback.TryExitThread(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugThread>()!);
            case RemoteCallback.NameChange:
                return callback.TryNameChange(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugThread>()!);
            case RemoteCallback.Break:
                return callback.TryBreak(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugThread>()!);
            case RemoteCallback.Breakpoint:
                return callback.TryBreakpoint(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugThread>()!, arguments.Proxy<ICorDebugFunctionBreakpoint>()!);
            case RemoteCallback.BreakpointSetError:
                return callback.TryBreakpointSetError(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugThread>()!, arguments.Proxy<ICorDebugFunctionBreakpoint>()!, arguments.UInt32());
            case RemoteCallback.StepComplete: {
                var domain = arguments.Proxy<ICorDebugAppDomain>()!;
                var thread = arguments.Proxy<ICorDebugThread>()!;
                var stepper = arguments.Proxy<ICorDebugStepper>()!;
                return callback.TryStepComplete(domain, thread, stepper, (CorDebugStepReason)arguments.UInt32());
            }
            case RemoteCallback.Exception: {
                var domain = arguments.Proxy<ICorDebugAppDomain>()!;
                var thread = arguments.Proxy<ICorDebugThread>()!;
                return callback.TryException(domain, thread, arguments.UInt32() != 0);
            }
            case RemoteCallback.EvalComplete:
                return callback.TryEvalComplete(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugThread>()!, arguments.Proxy<ICorDebugEval>()!);
            case RemoteCallback.EvalException:
                return callback.TryEvalException(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugThread>()!, arguments.Proxy<ICorDebugEval>()!);
            case RemoteCallback.DebuggerError:
                return callback.TryDebuggerError(arguments.Proxy<ICorDebugProcess>()!, (int)arguments.UInt32(), arguments.UInt32());
            case RemoteCallback.LogMessage: {
                var domain = arguments.Proxy<ICorDebugAppDomain>()!;
                var thread = arguments.Proxy<ICorDebugThread>()!;
                var level = (int)arguments.UInt32();
                var switchName = arguments.Text();
                return callback.TryLogMessage(domain, thread, level, switchName, arguments.Text());
            }
            case RemoteCallback.LogSwitch: {
                var domain = arguments.Proxy<ICorDebugAppDomain>()!;
                var thread = arguments.Proxy<ICorDebugThread>()!;
                var level = (int)arguments.UInt32();
                var reason = arguments.UInt32();
                var switchName = arguments.Text();
                return callback.TryLogSwitch(domain, thread, level, reason, switchName, arguments.Text());
            }
            case RemoteCallback.ControlCTrap:
                return callback.TryControlCTrap(arguments.Proxy<ICorDebugProcess>()!);
            case RemoteCallback.UpdateModuleSymbols:
                return callback.TryUpdateModuleSymbols(arguments.Proxy<ICorDebugAppDomain>()!, arguments.Proxy<ICorDebugModule>()!, 0);
            case RemoteCallback.Exception2: {
                if (callback is not ICorDebugManagedCallback2 callback2)
                    return null;
                var domain = arguments.Proxy<ICorDebugAppDomain>()!;
                var thread = arguments.Proxy<ICorDebugThread>()!;
                var frame = arguments.Proxy<ICorDebugFrame>()!;
                var offset = arguments.UInt32();
                var eventType = (CorDebugExceptionCallbackType)arguments.UInt32();
                return callback2.TryException(domain, thread, frame, offset, eventType, arguments.UInt32());
            }
            case RemoteCallback.ExceptionUnwind: {
                if (callback is not ICorDebugManagedCallback2 callback2)
                    return null;
                var domain = arguments.Proxy<ICorDebugAppDomain>()!;
                var thread = arguments.Proxy<ICorDebugThread>()!;
                var eventType = (CorDebugExceptionUnwindCallbackType)arguments.UInt32();
                return callback2.TryExceptionUnwind(domain, thread, eventType, arguments.UInt32());
            }
            default:
                // Function remaps, connections and MDAs have no place in the engine
                return null;
        }
    }
    // How many handles a callback's event begins with, for giving back the ones of a callback the debugger is not handed
    private static int HandleCount(RemoteCallback id) {
        switch (id) {
            case RemoteCallback.CreateProcess:
            case RemoteCallback.ExitProcess:
            case RemoteCallback.ControlCTrap:
            case RemoteCallback.DebuggerError:
            case RemoteCallback.CreateConnection:
            case RemoteCallback.ChangeConnection:
            case RemoteCallback.DestroyConnection:
                return 1;
            case RemoteCallback.Breakpoint:
            case RemoteCallback.BreakpointSetError:
            case RemoteCallback.StepComplete:
            case RemoteCallback.EvalComplete:
            case RemoteCallback.EvalException:
            case RemoteCallback.EditAndContinueRemap:
            case RemoteCallback.Exception2:
            case RemoteCallback.FunctionRemapComplete:
            case RemoteCallback.MDANotification:
                return 3;
            case RemoteCallback.FunctionRemapOpportunity:
                return 4;
            default:
                return 2;
        }
    }
    private void ReportExit(ICorDebugManagedCallback? callback) {
        ICorDebugProcess? exited;
        lock (stateLock) {
            exited = process;
            if (exitReported)
                return;
            exitReported = true;
        }
        if (callback != null && exited != null) {
            HostLog.Write("reporting ExitProcess to the debugger");
            callback.TryExitProcess(exited);
        }
    }
    private void OnDisconnected() {
        HostLog.Write("the agent disconnected");
        ICorDebugManagedCallback? callback;
        lock (stateLock)
            callback = managedCallback;
        ReportExit(callback);
    }
    private void Close() {
        RemoteDebuggerClient? connection;
        RemoteAgentListener? waiting;
        lock (stateLock) {
            connection = client;
            client = null;
            session = null;
            waiting = listener;
            listener = null;
        }
        connection?.Dispose();
        waiting?.Dispose();
    }
}
