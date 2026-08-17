# How a debug session flows through CorApi

The CLR debugging API is out-of-process: the debugger is a separate process that
receives events from, and issues commands to, a debuggee runtime. `DotNet.Debugging.CorApi`
provides every rung of that ladder; this page walks through them in the order a real
session uses them. `ICorDebug` semantics are Microsoft's — the linked Learn pages on
each type are the authoritative reference — this page explains how the pieces fit
together through this library.

## 1. Bootstrap: getting an `ICorDebug` with dbgshim

Nothing can be debugged until the debugger holds an `ICorDebug` instance bound to the
debuggee's runtime. That is `DbgShim`'s job — a static class of P/Invokes into the
native `dbgshim` library:

- **Launch**: `CreateProcessForLaunch(commandLine, bSuspendProcess: true, …)` starts the
  debuggee suspended and returns a resume handle. Starting suspended matters: if the
  CLR initializes before the debugger registers for startup notification, the
  notification is missed and registration can hang.
- **Startup notification**: `RegisterForRuntimeStartup(pid, pfnCallback, parameter, out token)`
  takes an unmanaged function pointer
  (`delegate* unmanaged[Cdecl]<void*, void*, int, void>`). When the CLR loads in the
  target process, dbgshim invokes the callback with a raw `ICorDebug*` and an HRESULT.
  `RegisterForRuntimeStartupEx` adds an application-group id (macOS sandboxing);
  `RegisterForRuntimeStartup3` additionally takes an `ICLRDebuggingLibraryProvider3` so
  the *debugger* can supply the runtime-matched `mscordbi` library — this is how
  debugging runtimes that are not installed on the machine works.
- **Resume**: once registered, `ResumeProcess(resumeHandle)` lets the debuggee run;
  `CloseResumeHandle` releases the handle. The startup callback then fires,
  `UnregisterForRuntimeStartup(token)` cancels the registration.
- **Attach to a running process**: same registration path by pid, or the manual route —
  `GetStartupNotificationEvent`, `EnumerateCLRs(pid)` (returns handle/path arrays,
  released with `CloseCLREnumeration`), `CreateVersionStringFromModule` for the loaded
  runtime, then `CreateDebuggingInterfaceFromVersionEx(CorDebugVersion_4_0, versionStr)`
  to obtain the `ICorDebug` directly.
- **Remote/mobile targets**: `RegisterForRuntimeStartupRemotePort(ip, port, platform, isServer,
  mscordbiPath, assemblyBasePath, out ICorDebug)` connects the same machinery over a
  TCP transport instead of a local pid.

## 2. Wiring the session

With an `ICorDebug` in hand:

```csharp
var callbacks = new CorDebugManagedCallback();
corDebug.Initialize();
corDebug.SetManagedHandler(callbacks);              // register the event sink
var process = corDebug.DebugActiveProcess(pid, win32Attach: false);
```

`CorDebugManagedCallback` is this library's implementation of
`ICorDebugManagedCallback`/`2`/`3`/`4` — the COM interfaces the *runtime calls back
into the debugger* on. It converts every callback into a .NET event with a typed
`EventArgs` class carrying the native arguments:

```csharp
callbacks.OnCreateProcess += ...;   // CreateProcessCorDebugManagedCallbackEventArgs
callbacks.OnLoadModule    += ...;   // LoadModuleCorDebugManagedCallbackEventArgs { Module }
callbacks.OnBreakpoint    += ...;   // { AppDomain, Thread, Breakpoint }
callbacks.OnStepComplete  += ...;   // { AppDomain, Thread, Stepper, Reason }
callbacks.OnException     += ...;   // legacy; OnException2 carries the full unwind info
callbacks.OnEvalComplete  += ...;
callbacks.OnAnyEvent      += ...;   // fires after every specific event
```

Every handler dispatch also raises `OnAnyEvent`, which is the natural place for the
"default continue" policy described next.

## 3. The stop/go model

`ICorDebug` is a *cooperative stop* API, and this is the single most important thing to
understand about it:

1. When the runtime delivers **any** managed callback, the debuggee is **stopped**.
   All of its managed threads stay frozen for as long as the debugger wants.
2. The debuggee resumes only when the debugger calls `Continue(false)` on the
   controller (`ICorDebugProcess` or `ICorDebugAppDomain`). Stops and continues are
   counted: nested `Stop`s require matching `Continue`s.
3. Consequently, *every callback must eventually be answered with a `Continue`* —
   either immediately (events the debugger doesn't surface to the user, e.g. module
   loads after bookkeeping) or later (a breakpoint stays stopped until the user
   resumes). Forgetting a `Continue` hangs the debuggee; continuing twice corrupts the
   session (`CORDBG_E_SUPERFLOUS_CONTINUE` in `Cor` names exactly that mistake).
4. `HasQueuedCallbacks` matters when deciding to resume: the runtime may have more
   events queued behind the current one (e.g. a step completion queued behind a
   breakpoint at the same location), and the debugger may want to drain or coalesce
   them instead of reporting both.

While the debuggee is stopped the debugger is free to inspect everything: threads,
frames, values, metadata. While it is running, almost every inspection call fails with
`CORDBG_E_PROCESS_NOT_SYNCHRONIZED` — which is why the `Try*`/HRESULT surface exists
and why the engine treats several HRESULTs as normal control flow rather than errors
(`CORDBG_E_PROCESS_TERMINATED` during shutdown, `CORDBG_E_CLASS_NOT_LOADED` /
`CORDBG_E_STATIC_VAR_NOT_AVAILABLE` when inspecting not-yet-initialized state).

## 4. Breakpoints and stepping

- **Breakpoints**: resolve a module + method (see metadata below), get its
  `ICorDebugFunction`, then `CreateBreakpoint()` on the function or
  `ICorDebugCode.CreateBreakpoint(ilOffset)` for a precise IL offset; `Activate(bool)`
  toggles it. When hit, the `OnBreakpoint` event delivers the thread and breakpoint;
  the session stays stopped.
- **Stepping**: `thread.CreateStepper()` yields an `ICorDebugStepper`. `Step(stepInto)`
  steps by instruction; `StepRange(stepInto, CorDebugStepRange[])` steps over an IL
  range (the ranges come from sequence points in the PDB, which is how source-line
  stepping is built); `StepOut()` runs to the caller. `SetUnmappedStopMask` and
  `SetInterceptMask` control stops in unmapped/intercepted code, and
  `SetJMC` (just-my-code, via `ICorDebugStepper2`) with
  `ICorDebugModule2.SetJMCStatus` confines stepping to user code. Completion arrives
  as `OnStepComplete` with a `CorDebugStepReason`.
- JIT behavior for debuggability is controlled per module with
  `ICorDebugModule2.SetJITCompilerFlags(CorDebugJITCompilerFlags)`.

## 5. Inspecting state

- **Threads and frames**: enumerate threads on the process; a stopped thread exposes
  chains (`ICorDebugChain`, managed/unmanaged) and frames. `ICorDebugILFrame` gives IL
  frames with `EnumerateLocalVariables()`/`EnumerateArguments()` and the current IL
  offset via `GetIP`; `ICorDebugILFrame4` (via cast) adds locals that only exist in
  optimized code re-materialized by the runtime.
- **Values**: everything is an `ICorDebugValue`, refined by cast to
  `ICorDebugReferenceValue` (`IsNull()`, `Dereference()`), `ICorDebugObjectValue`
  (fields via class + token), `ICorDebugStringValue`, `ICorDebugArrayValue`,
  `ICorDebugBoxValue` (`GetObject()`), `ICorDebugHandleValue` (GC-stable handles
  created with `ICorDebugHeapValue2.CreateHandle` so a value survives `Continue`), and
  `ICorDebugGenericValue`, which is the raw-bytes escape hatch: `GetValue`/`SetValue`
  read and write the primitive payload directly.
- **Types**: `ICorDebugValue2.GetExactType()` yields `ICorDebugType` — the exact
  instantiated type (element type, class, generic arguments via
  `EnumerateTypeParameters()`); `ICorDebugClass`/`ICorDebugClass2` represent the open
  type in its module, including `GetParameterizedType` to construct instantiations and
  static field access with `GetStaticFieldValue`.
- **The GC heap**: `ICorDebugProcess5` (cast from the process) exposes
  `EnumerateHeap()`/`EnumerateHeapRegions()`, per-object layout (`GetTypeLayout`,
  `GetTypeFields`), object graphs (`EnumerateGCReferences`), and object identity by
  address (`GetObject(CordbAddress)`) — the basis for "make object id" and heap-view
  features.

## 6. Metadata

Runtime objects answer "what is where", but names, signatures, and tokens come from
**metadata**. `ICorDebugModule.GetMetaDataInterface<IMetaDataImport>()` hands out the
metadata reader for a module (works for on-disk and in-memory/dynamic modules alike):

- enumeration uses the native cursor pattern — `ref HCorEnum` handle plus
  `EnumTypeDefs`/`EnumMethods`/`EnumFields`/… — surfaced through
  `IEnumerable<TypeDefToken>`-style extension adapters;
- `GetTypeDefProps`, `GetMethodProps`, `GetFieldProps`, `GetPropertyProps`,
  `GetMemberRefProps`, `GetTypeSpecFromToken`, … return names, attribute flags
  (`CorTypeAttr`, `CorMethodAttr`, `CorFieldAttr`), and raw signature blobs
  (`nint` + length) that callers parse per ECMA-335;
- tokens round-trip between the two worlds: metadata rows are `MethodDefToken`/`TypeDefToken`/…
  record structs, and runtime lookups accept them (`module.GetFunctionFromToken`,
  `class.GetStaticFieldValue(fieldDef, frame)`).

This pairing — runtime object from `ICorDebug*`, symbolic shape from `IMetaData*` — is
how the engine builds variable views, resolves breakpoint targets by name, and feeds
its expression evaluator.

## 7. Function evaluation

`ICorDebugEval` runs code *inside the stopped debuggee* — the mechanism behind watch
expressions, property getters, and `ToString` display values:

1. `thread.CreateEval()` on the thread that will host the evaluation.
2. `NewParameterizedObject`, `NewString`, `CreateValue`, … materialize arguments;
   `ICorDebugEval2.CallParameterizedFunction(function, typeArgs, args)` invokes with
   generics support.
3. The evaluation only actually runs after the debugger calls `Continue` — the runtime
   hijacks the chosen thread, and completion is reported like any other stop:
   `OnEvalComplete` with the result value, or `OnEvalException` with the thrown
   exception (`CORDBG_S_FUNC_EVAL_HAS_NO_RESULT` marks void evals).
4. `ICorDebugEval.Abort()`/`ICorDebugEval2.RudeAbort()` cancel runaway evaluations.

Evaluation while stopped at an arbitrary point can deadlock (the frozen threads may
hold locks the eval needs), which is why engines built on this API treat it carefully —
timeouts, abort paths, and `OnCustomNotification` are part of the discipline.

## 8. Ending a session

`OnExitProcess` announces debuggee termination (also delivered when the target dies
unexpectedly). For a deliberate stop, the debugger either `Terminate(exitCode)`s the
process or, for attach scenarios, `Stop`s, removes its breakpoints, and `Detach`es.
After `ExitProcess` the `ICorDebug` should be `Terminate()`d as well; any late call into
a dead target returns `CORDBG_E_PROCESS_TERMINATED`, which a well-behaved consumer
treats as "session over", not as a failure.
