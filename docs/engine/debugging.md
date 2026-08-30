# How a debug session flows through the engine

This page follows a session from the first request to the last through
`DotNet.Debugging.Engine`, naming the component that owns each step. The `ICorDebug`
mechanics themselves are described in [corapi/debugging.md](../corapi/debugging.md);
this page is about what the engine does with them.

## 1. Starting the debuggee

Nothing happens on `Launch`, `Attach` or `AttachRemote` — they only record the request, because
a client sends its breakpoints after the launch request and expects them bound from the very
first line. The work starts in `ConfigurationDoneAsync`:

- **Launch** (`LaunchProcessAsync`): the program is started with `DOTNET_DefaultDiagnosticPortSuspend=1`
  and redirected output (forwarded through `OnOutput`). The runtime then waits for a diagnostics
  client, which gives the debugger time to register for its startup: `DbgShimHost.AttachAsync`
  registers with dbgshim *before* `DiagnosticsClientHelper.ResumeRuntimeAsync` lets the runtime go,
  and the attach itself (`Initialize`, `SetManagedHandler`, `DebugActiveProcess`) runs inside
  dbgshim's startup callback while the runtime is still parked in its startup handshake.
- **Launch in a terminal** (`LaunchInTerminalAsync`): the host starts the program through the
  `OnTerminalLaunchRequested` event (the adapter's `runInTerminal` reverse request) and reports the
  pid in `LaunchRequest.ProcessId`; the rest is the launch path above.
- **Attach** (`AttachAsync`): the same registration by pid; the resume is attempted and its failure
  ignored, since a running process has nothing to resume.
- **Remote attach** (`AttachToRemote`, mobile/maccatalyst): `DbgShimHost.CreateRemote` builds an
  `ICorDebug` whose transport listens or connects per `RemoteAttachInfo`; `onListenerReady` lets
  the host launch the on-device app, and the `ICorDebugProcess` arrives later through the
  `CreateProcess` callback (`ProcessHandler`) instead of being returned.

Once attached, every known breakpoint is reported again (`SendBreakpointStatus`): the ones that
were `Pending` become `NotProcessed` until a module binds them.

## 2. The callback loop

The runtime raises `ICorDebug` callbacks on its own thread and stays stopped until the debugger
continues. `CorDebugManagedCallback.OnAnyEvent` only queues the callback on a `Channel`;
`ProcessEventQueueAsync` takes the lock and dispatches it to the matching handler in `Handlers/`.
Every handler ends in one of two ways:

- `ContinueProcess()` — the plain `ICorDebugProcess.Continue`, for callbacks that do not stop
  (module loads, thread creation, an unhandled callback kind, or any callback arriving while a
  function evaluation runs);
- raising `OnStopped` / `OnExceptionThrown` and returning, which leaves the debuggee stopped
  until the host calls `Continue()` or `StepAsync()`.

The public `Continue()` additionally clears the variables references and frame ids, as the values
behind them are gone once the debuggee runs (`ClearReferences`), and tolerates
`CORDBG_E_SUPERFLOUS_CONTINUE`.

## 3. Modules and symbols

`ModuleHandler.HandleModuleLoaded` builds a `ModuleInfo` for every `LoadModule` callback:

- `ModuleMetadataReader.TryLoad` reads the PE metadata (from disk, or from the debuggee's memory
  for in-memory modules) and the portable PDB — an external `.pdb` next to the assembly whose
  id matches the CodeView entry, or an embedded one.
- The *user code* heuristic: a module jitted with `CORDEBUG_JIT_DISABLE_OPTIMIZATION` or
  `CORDEBUG_JIT_ENABLE_ENC` was built by the user. With `JustMyCode`, such modules with symbols get
  `SetJMCStatus(true)` so the stepper never stops elsewhere.
- `System.Private.CoreLib` creates the `ExpressionEvaluator` (it needs the primitive type classes),
  which is why evaluation is available at every stop.
- `BreakpointManager.BindPending(module)` binds what the new module resolves and `OnBreakpointChanged`
  reports the newly verified breakpoints; a pending `stopAtEntry` places its one-shot breakpoint on
  the entry point's first sequence point.

## 4. Breakpoints

`SetBreakpoints(file, requests)` replaces the file's breakpoints; `SetFunctionBreakpoints` replaces
all function breakpoints. Binding (`BreakpointManager.TryBind`) asks every module with symbols to
`ResolveBreakpoint(file, line, column)`:

- `ModuleMetadataReader.FindDocument` matches the document by full path first, then by file name
  (PDBs built elsewhere);
- `SequencePointResolver` picks the sequence point: the one covering the position with the latest
  start (which naturally selects the innermost lambda), netcoredbg's containment rule when several
  start at the same position, and the next line with code when none covers it;
- the `ICorDebugFunctionBreakpoint` is created at that IL offset and the `Breakpoint` gets the
  resolved `Line`/`Column`/`EndLine`/`EndColumn` and a `Location` carrying the document's checksum
  and Source Link.

Function breakpoints parse `Namespace.Type<T>.Method<U>(int, string)` into a
`FunctionBreakpointPattern` and bind every matching method of every module, at its first sequence
point; a breakpoint can therefore hold several bindings.

When a breakpoint callback arrives (`BreakpointHandler.HandleBreakpointAsync`) the decisions are
made in this order: continue if an evaluation is running; if a step is in progress, continue when
the step is already complete (its `StepComplete` callback is queued right behind and reports the
stop) or cancel the step otherwise; give the `AsyncStepper` its breakpoints; handle the entry
breakpoint; then for a user breakpoint count the hit, check the hit condition
(`BreakpointManager.CheckHitCondition`: `3`, `==3`, `>=3`, `<3`, `%3`), evaluate the condition,
print a logpoint (`OnLogPoint`, `{expression}` placeholders evaluated in the top frame) and
continue, or raise `OnStopped` with `StopReason.Breakpoint` and the breakpoint id.

## 5. Stepping

`StepAsync(threadId, kind)` is handled by `StepController`:

- `CreateStepper` builds an `ICorDebugStepper` that intercepts everything except class initializers
  and security stubs, never stops at unmapped code, honours JMC, and steps the whole *statement*:
  the IL range from the current sequence point to the next (`ModuleMetadataReader.TryGetStepRange`)
  rather than one instruction. Step out is `StepOut()`.
- On `StepComplete` (`TryCompleteStep`) the engine decides whether this is a place to stop: a frame
  in a module without symbols stops without a location; a frame with symbols but no sequence point
  at the offset (compiler-generated code) keeps stepping into; a `STEP_CALL` landing in a callee's
  prolog steps over to its first statement; past the last statement of a
  `DebuggerHidden`/`DebuggerStepThrough`/`DebuggerNonUserCode` method it keeps stepping. Otherwise
  `OnStopped(StopReason.Step)` carries the `SourceLocation`.

`AsyncStepper` handles `await`: a plain step over an await would run until the method returns to
its caller. Using the async stepping information Roslyn writes to the PDB (yield/resume offsets),
it plants a breakpoint at the next *yield* point while the plain stepper runs. If the yield point is
reached the step is cancelled, the builder's `ObjectIdForDebugger` is captured, and a breakpoint is
moved to the *resume* point; when that one is hit by the same invocation (same thread, or the same
builder id on another thread) an ordinary step resumes from there. Stepping out of an async method
asks the builder for `SetNotificationForWaitCompletion(true)` and breaks in
`Task.NotifyDebuggerOfWaitCompletion`, from where a step out lands in the resumed caller.

## 6. Stops and exceptions

`OnStopped(StopInfo)` reports breakpoints, steps, the entry stop, `Debugger.Break()` and `Pause`.
`Pause` is the one stop the engine raises itself: `ICorDebugProcess.Stop` produces no callback, so
after stopping it reports `StopReason.Pause` for the requested thread. A pause is refused while
the process is not running — including the moment right after an attach, when the runtime is
still delivering its synthetic attach callbacks.

Exceptions come through two callbacks. `Exception` (first chance or unhandled) raises
`OnExceptionThrown` with `FirstChance`/`Unhandled`; the host applies its filters and calls
`Continue()` when it does not want the stop. `Exception2` follows the dispatch: a first-chance
notification in a user-code frame marks the thread, and when the catch handler is found in
non-user code for a marked thread the engine raises `UserUnhandled` — an exception that passed
through user code and is about to be swallowed by a library.

## 7. Inspecting state

- `GetThreads` names threads from the managed `Thread._name` field (read directly, no evaluation)
  or, except for the main thread, the OS thread name (`NativeThreadNames`), and marks the first
  thread as main so the host can label it.
- `GetStackFrames` walks the managed chains of the thread; each `StackFrameInfo` carries the
  `Namespace.Type.Method(params)` signature read from metadata, the module, the `SourceLocation`
  (with checksum and Source Link) and the native instruction pointer; internal and native frames
  are reported by kind.
- `GetLocalsReference(frameId)` returns the reference of the frame's scope, zero when there is
  nothing to show. `GetVariablesAsync` then goes through `VariableProvider`:
  the current `$exception`, `this` and the arguments (for lambdas and async methods `this` is the
  generated closure or state machine, whose captured `this` and hoisted locals are listed instead),
  then the IL locals named by the PDB scopes at the current offset.
- Expanding a value lists its instance fields and properties, base types included up to
  `Object`/`ValueType`/`Enum`; properties are read by evaluating their getters. Every type shows its
  public members inline plus a `Non-Public members` group when non-public ones exist, and
  statics go into a `Static members` group. A `DebuggerTypeProxy` instance stands in for the
  value's own members with the real ones under `Raw View`; `DebuggerBrowsable(Never)` hides,
  `RootHidden` inlines an array's elements.
- `ValueFormatter` produces the text: escaped strings, `{int[60]}` arrays, enum names and `A | B`
  flags, `int?` nullables, decimals, and `{TypeName}` for objects. Types with a `DebuggerDisplay`
  attribute or a `ToString` override hand back a template that `VariableProvider` evaluates in the
  debuggee through the expression evaluator.
- `SetVariableAsync` (`VariableWriter`) assigns primitives and `null`; `SetNextStatement` moves the
  instruction pointer to the sequence point of a line in the current method.

## 8. Expression evaluation

`EvaluateAsync(expression, frameId)`:

1. `ExpressionCompiler` compiles the expression with the Roslyn expression compiler
   (`Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator`, publicized) against metadata blocks built
   from the loaded modules' metadata — one module per assembly identity, preferring the frame's —
   in the frame's method context (locals, hoisted locals, constants from the PDB) or in a type
   context for `DebuggerDisplay` templates. Compiled expressions are cached until a module loads.
2. `CilInterpreter` executes the emitted method: arithmetic, conversions, branches and string
   interpolation run on the host; field reads, array access, allocations and calls go to the
   debuggee through `FuncEvalRunner`, which starts the `ICorDebugEval`, continues the process and
   pumps callbacks until the evaluation completes. The debugger intrinsics the compiler emits
   (`CreateVariable`, `GetObjectByAlias`, `GetException`) are handled in the interpreter.
3. The result is materialized as a debuggee value; an `EvaluationResult` owns the strong handle that
   keeps a reference result alive, and `EvaluateAsync` keeps it (`KeepHandle`) when the value is
   expandable, so the variables reference can release it later.

Breakpoint conditions, logpoints and `DebuggerDisplay`/`ToString` values use the same path, with
errors turned into "condition not met" or an error-marked variable rather than a failed request.

## 9. Source Link

`ModuleMetadataReader` reads the Source Link custom debug information of the PDB
(`SourceLinkMap`: document path patterns to URLs, the longest matching pattern wins) and every
`SourceLocation` carries the URL of its document when there is one. That is all the engine does
with it. The adapter reports a document that does not exist locally with its PDB path, the URL
(`vsSourceLinkInfo`) and a `sourceReference` — a handle the `SourceLinkResolver` keeps per URL for
the whole session, when `sourceLinkOptions` allow the URL. Nothing is downloaded during the stack
trace: the client asks for the content with the `source` request when it opens the document
(`SourceHandler`), the resolver downloads it once and serves it from memory afterwards. Breakpoints
and jumps in such a document come back with the PDB path, which the engine resolves as usual.

## 10. Ending a session

`Terminate` stops a running process (`Terminate` needs it synchronized), terminates it and
disposes; `Disconnect(terminateDebuggee: false)` stops it and detaches, leaving it running.
`Dispose` releases the module metadata, deactivates every breakpoint, abandons the steppers, clears
the references, unsubscribes from the callbacks and completes the event queue — draining it by hand
because the event loop is waiting for the very lock `Dispose` runs under — and kills a process the
engine launched itself when terminating.
