# Stepping

`Stepping/StepController` owns the one `ICorDebugStepper` a step uses and decides where a completed
step stops; `Stepping/AsyncStepper` carries steps across `await` points, which a plain stepper
cannot do. Both are driven from `ManagedDebugger.StepAsync` and the `StepComplete`/`Breakpoint`
callback handlers.

## A plain step

```
StepAsync(threadId, kind)
  ├─ AsyncStepper.TrySetupAsync(thread, kind)      ── may take the step over entirely (async step out)
  ├─ StepController.CreateStepper(thread, kind)    ── otherwise (or additionally, see below)
  ├─ ClearReferences()
  └─ ContinueProcess()
```

`CreateStepper` refuses to run when the active frame is not an IL frame or a step is already in
progress, then configures the stepper:

| Setting | Value | Why |
|---|---|---|
| `SetInterceptMask` | `INTERCEPT_ALL` minus `INTERCEPT_SECURITY` and `INTERCEPT_CLASS_INIT` | Stop in interceptors (exception filters, stubs) except static constructors and security stubs. |
| `SetUnmappedStopMask` | `STOP_NONE` | Never stop at native code without IL mapping. |
| `SetJMC` | `true` when `JustMyCode` | The runtime stepper skips code that is not marked user code (`ModuleHandler` marks user modules with symbols via `SetJMCStatus`). |
| Range | `StepRange(stepInto, [start, end))` | Step the whole *statement*: `ModuleMetadataReader.TryGetStepRange` gives the current sequence point's offset and the next one's; when they coincide (last statement) the range ends at the IL code size. Without sequence points, a single-instruction `Step(stepInto)`. |
| Step out | `StepOut()` | — |

### Where a completed step stops

`StepComplete` arrives with a `CorDebugStepReason`; `StepController.TryCompleteStep` first abandons
an active async step (the plain step finished first, so the method left before reaching the
await) and the stepper itself, then decides:

| Frame after the step | Decision |
|---|---|
| Not an IL frame | Stop, no source location. |
| Module without symbols | Stop, no source location — the client shows the frame as is. (Only reachable with `JustMyCode` off.) |
| Symbols, but no sequence point at the IP | Step **into** again: compiler-generated code such as an async state machine's glue. |
| IP unmapped / no mapping info | Error — logged, continued. |
| `STEP_CALL` and the IP is before the next sequence point | Step **over** the callee's prolog to reach its first statement. |
| Past the last sequence point of a `DebuggerHidden`/`DebuggerStepThrough`/`DebuggerNonUserCode` method | Step **into** again. |
| Otherwise | `OnStopped(StopReason.Step)` with the current `SourceLocation`. |

Re-steps go through `CreateStepper` and `ContinueProcess` without reporting anything.

### Breakpoints during a step

`BreakpointHandler` checks the stepper before anything else: if the stepper is no longer active the
step has completed at the breakpoint's location and its `StepComplete` callback is queued behind the
breakpoint — the breakpoint is continued and the step reports the stop. If the stepper is still
active the breakpoint was hit before the step destination (inside a stepped-over call) and wins: the
step is cancelled and the breakpoint stop is reported. `Pause`, `Debugger.Break()` and exception stops
cancel everything (`Disable`).

## Stepping across `await`

Roslyn records *async method stepping information* in the PDB for every async method: the IL
offsets of each await's **yield** (the state machine is about to return to its caller) and
**resume** (the continuation re-enters `MoveNext`) points, which `ModuleMetadataReader.GetAsyncMethodInfo`
reads together with the offset of the method's last statement. A plain step over an await would run
to the caller, so the step is carried by breakpoints:

```
TrySetupAsync(thread, kind)
  ├─ not an async method, or no symbols                → not handled (plain step)
  ├─ IP at or past the last statement (not in prolog/epilog) → kind becomes Out
  ├─ kind == Out                                       → see "Stepping out" below
  ├─ no await after the IP                             → not handled (plain step)
  └─ breakpoint at the next yield point; status Yield  → plain stepper ALSO created
                                                         (it ends the step if the method leaves first)

Breakpoint callback → AsyncStepper.TryHandleBreakpointAsync
  ├─ the NotifyDebuggerOfWaitCompletion breakpoint     → StepOut (controller steps out of it)
  ├─ no active async step / another breakpoint / IP mismatch → not handled (async step cleared)
  ├─ status Yield, same thread                         → capture the builder id, breakpoint moves
  │                                                      to the resume point, status Resume → Continue
  └─ status Resume                                     → same invocation? new plain stepper of the
                                                         original kind, async step cleared → Continue
                                                         (another invocation: keep waiting → Continue)
```

"Same invocation" is the thread that yielded, or — since continuations run on any thread — the same
`ObjectIdForDebugger` on the method builder (`AsyncTaskMethodBuilder<T>` and friends expose it for
exactly this). The id is read once at the yield point through a func eval, kept in a strong handle,
and compared by address at the resume point; an address of zero on either side is accepted as a
match. The yield breakpoint is only honoured on the thread that set the step up.

"Next await" is the first await whose yield offset is at or after the IP; when the IP is inside an
await block (between a yield and its resume) there is no next await and the plain stepper handles
the step.

### Stepping out of an async method

Stepping out has to wait for the method's *task* to complete, not for `MoveNext` to return. The
builder (the `<>t__builder` field of the state machine `this`) is asked to
`SetNotificationForWaitCompletion(true)` — a func eval — which makes the runtime call
`Task.NotifyDebuggerOfWaitCompletion` when the awaiting code resumes; a breakpoint at IL offset 0 of
that method (in `System.Private.CoreLib`) catches it, and the handler turns the hit into an ordinary
step out, which lands in the resumed caller. `AsyncVoidMethodBuilder` has no task to wait for, so
`async void` methods get a plain step out; if any part of the setup fails the plain step out is
used as well.

## Cleanup

`StepController.Disable` (on pause, `Debugger.Break()`, exceptions and disposal) deactivates the
stepper, drops the async step with its yield/resume breakpoint and handle, and the notification
breakpoint. `AsyncStepper.ClearActiveStep` alone runs on every `StepComplete`.
