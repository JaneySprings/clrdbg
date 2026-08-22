# DotNet.Debugging.Engine

`DotNet.Debugging.Engine` is the debugger proper: it drives a .NET (Core) debuggee through
the `ICorDebug` API that [`DotNet.Debugging.CorApi`](../corapi/README.md) projects into C#,
and exposes what a client needs — stops, threads, frames, variables, evaluation, breakpoints —
as plain managed objects. It knows nothing about the Debug Adapter Protocol: the DAP shaping
(presentation hints, `Module.dll!Type.Method Line N` frame names, breakpoint messages, paging)
lives in `DotNet.Debugging.Adapter`, which maps the engine's models with `ToXxx()` extension
methods in `Adapter/Extensions/`.

```
Client (VS Code) ──DAP──> DotNet.Debugging.Adapter ──models/events──> DotNet.Debugging.Engine ──ICorDebug──> debuggee
```

## Layout

| Folder | Contents |
|---|---|
| `ManagedDebugger.cs` | The facade: public requests, events, the lifecycle (launch/attach/configure/continue/pause/step/terminate) and the runtime event loop. The only public entry point. |
| `Handlers/` | Partials of `ManagedDebugger` handling the `ICorDebug` managed callbacks, one file per callback group (`BreakpointHandler`, `ExceptionHandler`, `ModuleHandler`, `ProcessHandler`, `StepHandler`, `ThreadHandler`) — the engine-side counterpart of the adapter's `Handlers/` request partials. |
| `Breakpoints/` | `BreakpointManager` (registry, binding to loaded modules, hit-count conditions), `FunctionBreakpointPattern` / `FunctionBreakpointResolver` (`Type.Method<T>(int, string)` patterns matched against metadata). |
| `Stepping/` | `StepController` (the `ICorDebugStepper` of a step, the rules deciding where a completed step stops) and `AsyncStepper` (steps across `await` points carried by breakpoints). |
| `Variables/` | `VariableProvider` (locals, arguments, members, array elements, groups, `DebuggerTypeProxy`), `ValueFormatter` and `TypeNameFormatter` (value and type text the way the C# debugger shows them), `VariableWriter` (assignments), `VariableManager` / `FrameReferenceManager` (the handles issued to the client and the debuggee handles kept alive behind them). |
| `Evaluation/` | The expression evaluator: `ExpressionCompiler` compiles C# with the Roslyn expression compiler against the loaded modules' metadata, `CilInterpreter` executes the emitted CIL, running anything that touches the debuggee through `FuncEvalRunner` (`ICorDebugEval`). |
| `Metadata/` | `ModuleMetadataReader` (PE metadata + portable PDB: sequence points, local names, async stepping info, Source Link, checksums), `SequencePointResolver` (source position → IL offset), `SourceLinkMap`, signature providers. |
| `Interop/` | `DbgShimHost` (runtime startup registration and the remote transport through dbgshim), `DiagnosticsClientHelper` (resuming a diagnostics-suspended runtime), `NativeThreadNames` (OS thread names). |
| `Models/` | The objects handed to the host: `StopInfo`, `ExceptionStopInfo`, `ThreadInfo`, `StackFrameInfo`, `SourceLocation`, `VariableInfo`, `Breakpoint`, `ModuleInfo`, `ExceptionInfo`, `LaunchInfo`, `RemoteAttachInfo`, the request objects. |
| `Enums/` | `StopReason`, `StepKind`, `BreakpointStatus`, `VariableKind`, `VariableVisibility`, `ExceptionStopKind`, `StackFrameKind`, `ConsoleType`. |
| `Extensions/` | Helpers over `CorApi` for the engine: value unwrapping (`UnwrapDebugValue`), field lookup by name, metadata token attributes and type lookup. |
| `Logging/` | `ICustomLogger` and the static `DebuggerLoggingService` the host plugs its logger into. |

## The API model

**One facade, plain models.** A host creates a `ManagedDebugger`, subscribes to its events and
calls its requests. Requests return model objects or `Task`s of them; nothing in the signatures
is protocol-specific:

```csharp
DebuggerLoggingService.CustomLogger = new EngineLogger();   // before anything logs
var debugger = new ManagedDebugger();
debugger.OnStopped += stop => SendStopped(stop.ThreadId, stop.Reason, stop.HitBreakpointIds);
debugger.OnModuleLoaded += module => SendModule(module.ToModule(id, justMyCode));

debugger.JustMyCode = true;
debugger.Launch(new LaunchInfo(program) { Arguments = args, StopAtEntry = false });
var breakpoints = debugger.SetBreakpoints(path, requests);   // bound when the module loads
await debugger.ConfigurationDoneAsync();                     // the launch happens here

var frames = debugger.GetStackFrames(threadId);              // StackFrameInfo: Name, ModuleName, Location, InstructionPointer
var reference = debugger.GetLocalsReference(frames[0].Id);   // 0 when the frame has nothing to show
var locals = await debugger.GetVariablesAsync(reference);    // VariableInfo: Name, Value, Type, Kind, Visibility, VariablesReference
var result = await debugger.EvaluateAsync("items.Count * 2", frames[0].Id);
```

**Events carry engine facts, not protocol verbs.** `OnStopped(StopInfo)` says *why* and *where*
(`StopReason.Breakpoint` with the hit breakpoint ids, `Step`, `Pause`, `Entry`, plus the
`SourceLocation`); `OnExceptionThrown(ExceptionStopInfo)` reports the kind
(`FirstChance`, `UserUnhandled`, `Unhandled`) and type name and leaves the decision to the
subscriber — it either does nothing (the debuggee stays stopped) or calls `Continue()`.
`OnModuleLoaded(ModuleInfo)`, `OnBreakpointChanged(Breakpoint)`, `OnThreadStarted/Exited`,
`OnProcessStarted`, `OnExited`, `OnOutput` and `OnLogPoint` complete the set.

**Handles instead of objects.** Frame ids and variables references are integers issued by the
engine (`FrameReferenceManager`, `VariableManager`). They are the engine's own bookkeeping, not
DAP numbers: a frame is re-obtained from its thread and depth on every use because `ICorDebugFrame`
objects are neutered whenever the debuggee runs, and a variables reference keeps the strong
`ICorDebugHandleValue` behind an expanded value alive until the next continue clears it.

**Statuses, not messages.** A `Breakpoint` exposes `BreakpointStatus` (`Pending`, `NotProcessed`,
`NoSymbols`, `NoMatchingFunctions`, `Bound`, `Error` with `Error` text); a `VariableInfo` exposes
`Kind`, `Visibility` and `IsError`; a `StackFrameInfo` exposes `Kind`, `ModuleName`, the method
signature and a `SourceLocation`. The user-facing strings and the composed display names are the
adapter's (`Resources`, `DebuggerExtensions`, `ServerExtensions`).

## Concurrency

Everything the engine does happens under one `SemaphoreSlim`:

- The `ICorDebug` managed callbacks are raised on the runtime's own thread; the engine only
  queues them on a `Channel` and a background loop dispatches them one at a time under the lock
  (`ManagedDebugger.ProcessEventQueueAsync` → `DispatchEventAsync` → `Handlers/`).
- Host requests go through `InvokeAsync`, which takes the lock, first drains the callbacks queued
  so far (so a request never sees a stale stop state) and then runs the request.
- A function evaluation continues the debuggee while a request holds the lock;
  `FuncEvalRunner` pumps the callbacks arriving meanwhile (`WaitForEvalEventAsync`) until the
  `EvalComplete`/`EvalException` one, and the handlers know to continue through any stop
  that arrives while `IsEvaluating`.

Two consequences shape the code: frames and non-handle values obtained before an `await`
must not be used after it, and there is no inner lock anywhere else — the managers are plain
dictionaries.

## Logging

The engine never writes to the standard streams (a debug adapter owns them). It logs through
`DebuggerLoggingService.LogMessage` / `LogError`, which forward to the `ICustomLogger` the host
assigns to `DebuggerLoggingService.CustomLogger` — the adapter sets `Logging/EngineLogger`
before creating the session. With no logger set, nothing is logged.

## Documents

| Document | Contents |
|---|---|
| [debugging.md](debugging.md) | How a session flows through the engine: starting the debuggee, the callback loop, modules and symbols, breakpoints, stepping (including async), stops and exceptions, inspecting state, expression evaluation, Source Link, ending a session |
| [runtime.md](runtime.md) | Process lifecycle and the callback loop: dbgshim startup registration, diagnostics-suspended launches, remote transports, the event queue and lock, stop/continue/pause, threads, exit and disposal |
| [breakpoints.md](breakpoints.md) | `BreakpointManager`: the model and statuses, binding, sequence point selection, function breakpoint patterns, what happens on a hit, the entry-point breakpoint |
| [stepping.md](stepping.md) | `StepController` and `AsyncStepper`: stepper configuration, where a completed step stops, breakpoints during steps, stepping across `await` and out of async methods |
| [variables.md](variables.md) | `VariableProvider`: references and handles, a frame's scope, expanding values (members, groups, proxies), formatting rules, assignments |
| [evaluation.md](evaluation.md) | The expression evaluator: Roslyn compilation against the loaded modules, the CIL interpreter, token resolution, func evals, results and errors |
| [metadata.md](metadata.md) | `ModuleMetadataReader` and `ModuleInfo`: symbol discovery, what the PDB answers, Source Link maps, signature providers, live metadata access |
