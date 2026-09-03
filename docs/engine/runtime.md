# Process lifecycle and the callback loop

`ManagedDebugger` owns three native objects — the `ICorDebug` obtained from dbgshim, the
`ICorDebugProcess` it debugs and the `CorDebugManagedCallback` the runtime reports into — plus,
for a launch, the `System.Diagnostics.Process` it started. This page covers how those come to
exist, how the runtime's callbacks reach the engine, and how a session ends.
The request-by-request walk-through is in [debugging.md](debugging.md).

## 1. Obtaining the process

All three start paths — `LaunchAsync`, `AttachAsync` and `AttachRemote` — start the debuggee when
called; the host calls them after the client has sent its breakpoints (the adapter's `configurationDone`).

### Local processes: `DbgShimHost.AttachAsync`

Attaching to a local process — launched or not — goes through dbgshim's runtime-startup
notification rather than a direct `ICorDebug` creation:

```
RegisterForRuntimeStartup(pid, &OnRuntimeStartup)   ── registration (synchronous)
ResumeRuntimeAsync(pid)                              ── only for a diagnostics-suspended debuggee
        │  runtime starts, dbgshim's helper thread invokes
        ▼
OnRuntimeStartup(ICorDebug*)  [UnmanagedCallersOnly]
    Initialize(); SetManagedHandler(callbacks); process = DebugActiveProcess(pid)
    completion.SetResult()                           ── or SetException: nothing may escape the callback
        │
        ▼
await completion; UnregisterForRuntimeStartup(token)
```

Two details are load-bearing:

- **The attach happens inside the callback.** dbgshim lets the parked runtime continue the moment the
  callback returns. On Unix the runtime then marks itself debugger-attached and blocks until the
  debugger continues, so a late `DebugActiveProcess` would still work — but on Windows nothing holds it,
  and a short program can be exiting by the time a post-callback attach reaches it (`E_ACCESSDENIED`).
  Attaching while the runtime is still in its startup handshake works everywhere.
- **Registration precedes the resume.** A launched debuggee is started with
  `DOTNET_DefaultDiagnosticPortSuspend=1`, so its runtime waits for a diagnostics client before
  starting; `DbgShimHost.AttachAsync` registers synchronously before returning its task, then
  `DiagnosticsClientHelper.ResumeRuntimeAsync` sends `ResumeRuntime` over the diagnostics IPC — retried
  with a doubling delay, because hosts that start the runtime themselves (e.g. godot) open the IPC
  late. For a plain attach the resume is attempted and its failure ignored: a running runtime has
  nothing to resume. The completion source runs its continuation asynchronously so the engine never
  continues on dbgshim's helper thread, which `Unregister` waits for.

Only one registration can be in flight (a static slot); a second concurrent attach throws.

A **terminal launch** (`ConsoleType.IntegratedTerminal`/`ExternalTerminal`) asks the host to start
the program through the `OnTerminalLaunchRequested` event — off the request thread, as the adapter's
implementation blocks on the client's `runInTerminal` response — and attaches to the pid the
subscriber sets on the `LaunchRequest`, the same way.

### Remote processes: `DbgShimHost.CreateRemote`

Mobile and maccatalyst targets have no local pid. `RegisterForRuntimeStartupRemotePort(address, port,
platform, isServer, mscordbiPath, assembliesPath)` returns an `ICorDebug` whose transport already
listens (or connects) on the given port using the target-matched `libmscordbi`. The engine sets the
managed handler, invokes `onListenerReady` — the adapter launches the on-device app with the
`CORECLR_REMOTE_DEBUGGER_*` environment that makes its profiler connect back — and calls
`DebugActiveProcess(0)`, which is expected to throw: the `ICorDebugProcess` arrives through the
`CreateProcess` callback instead (`ProcessHandler.HandleProcessCreated`), and only then is `ProcessId`
known.

### Debuggee output

A launched process has stdout/stderr redirected; each line is forwarded through `OnOutput` with a
newline appended. The data-received callbacks run on background threads, so a throwing subscriber is
caught and logged rather than taking the adapter down. Attached processes keep their own console.

## 2. The callback loop

```
runtime thread ──► CorDebugManagedCallback.OnAnyEvent ──► Channel<CorDebugManagedCallbackEventArgs>
                                                                   │
   ProcessEventQueueAsync:  WaitToReadAsync ─► lock ─► TryRead ─► DispatchEventAsync ─► unlock
   InvokeAsync(request):    lock ─► drain queue (DispatchEventAsync each) ─► request ─► unlock
   FuncEvalRunner:          (lock held) Continue ─► WaitForEvalEventAsync: DispatchEventAsync until EvalComplete/EvalException
```

- The runtime raises callbacks on its own thread and stays stopped until the debugger continues.
  The engine does nothing on that thread but queue the event.
- The background loop dispatches one event at a time under the `SemaphoreSlim`. A request
  (`InvokeAsync`) takes the same lock and first drains whatever is queued, so the state it inspects
  is current — a stop that has happened is never reported after the request that should have seen it.
- `DispatchEventAsync` routes by event type (`Handlers/`); every handler finishes either with
  `ContinueProcess()` (the debuggee keeps running) or by raising `OnStopped`/`OnExceptionThrown`
  (the debuggee stays stopped). `EvalComplete`/`EvalException` are not continued: the evaluation that
  started them is waiting for them. Any other callback kind is continued unchanged.
- A handler that throws is logged and, if the process is stopped, continued — a broken handler must
  not leave the debuggee hung.
- While a function evaluation runs (`IsEvaluating`), `WaitForEvalEventAsync` keeps dispatching the
  callbacks that arrive — module loads, thread creation, but also breakpoints and exceptions hit by
  the evaluated code, which `BreakpointHandler`/`ExceptionHandler` continue straight away.

The lock is the only synchronization in the engine. The managers are plain dictionaries, and the
rule that follows from "everything runs under the lock, sometimes across an `await`" is: objects
obtained from `ICorDebug` before an `await` (frames, non-handle values) are stale after it, because
the await may have continued the debuggee.

## 3. Stopping and continuing

| Operation | What happens |
|---|---|
| `ContinueProcess()` (internal) | `ICorDebugProcess.Continue(false)`, nothing else. Used by handlers and internal re-steps. |
| `Continue()` | Clears the variables references (releasing their debuggee handles) and frame ids, then continues, tolerating `CORDBG_E_SUPERFLOUS_CONTINUE`. |
| `StepAsync(threadId, kind)` | Sets the step up (`StepController`), clears references, continues. See [stepping.md](stepping.md). |
| `Pause(threadId)` | `ICorDebugProcess.Stop(0)` and a synthetic `OnStopped(StopReason.Pause)`: `Stop` produces no callback. Refused with an exception when `IsRunning` is false — including the moment right after an attach, when the runtime still reports itself stopped while delivering its synthetic attach callbacks; the state clears in a moment and the client can ask again. |

`IsRunning` is `ICorDebugProcess.IsRunning`, with a failed read counting as not running.

## 4. Threads

`CreateThread`/`ExitThread` maintain the `threads` dictionary (the first thread created is the main
thread), and `GetThread(id)` answers from it: `ICorDebugThread` objects stay valid until the thread
exits, and `ICorDebugProcess.GetThread` is not implemented by the remote (mobile) transport. Thread
names are read without running code: the managed `Thread._name` field of the thread object, then —
for every thread but the main one, whose OS name is the executable's — the OS thread name
(`NativeThreadNames`: `proc_pidinfo` on macOS, `/proc/<pid>/task/<tid>/comm` on Linux,
`GetThreadDescription` on Windows). `ThreadInfo.IsMain` lets the host label an unnamed main thread.

## 5. Exit and disposal

`ExitProcess` reports `OnExited` with the exit code of a launched process — the runtime is shutting
down, so the engine waits up to two seconds for the OS process to finish before reading it; an
attached process reports 0. Nothing is continued afterwards.

`Terminate()` stops a running process first (`ICorDebugProcess.Terminate` requires a synchronized
process), terminates it and disposes. `Disconnect(terminateDebuggee: false)` stops and detaches.
`Dispose` then, in order: releases the module metadata readers, deactivates every breakpoint
(`BreakpointManager.Clear`, stopping at `CORDBG_E_PROCESS_TERMINATED`), drops the entry breakpoint,
abandons the steppers, clears the references, unsubscribes from the callbacks and completes the
event queue — draining it by hand, because the event loop is blocked on the very lock `Dispose`
runs under and exits once that lock is released — detaches, and kills the launched process when
terminating.
