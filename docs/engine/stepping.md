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
| Module without symbols, reached by `STEP_CALL` (a step into) | Step **out** again, like a filtered method: vsdbg does not stop where no source can be shown. (Only reachable with `JustMyCode` off.) |
| Module without symbols, reached otherwise | Stop, no source location — the client shows the frame as is. |
| Symbols, but no sequence point at the IP | Step **into** again: compiler-generated code such as an async state machine's glue. |
| IP unmapped / no mapping info | Error — logged, continued. |
| A method (or declaring type) marked `DebuggerHidden`/`DebuggerStepThrough` — plus `DebuggerNonUserCode` when `JustMyCode` is on, vsdbg ignores it otherwise — at any offset | Step **into** again, marked as skipping: the step lands in the first user code the method calls or leaves it altogether. |
| `STEP_CALL` into a property accessor or an operator method, with `EnableStepFiltering` (default on) | Step **out** again, marked as skipping — "step over properties and operators". |
| `STEP_CALL` and the IP is before the next sequence point | Step **over** the callee's prolog to reach its first statement. |
| IP in a hidden region that is cleanup between two statements: inside a `finally` handler (the one a `using`/`lock` compiles to), the plumbing between two nested finallys while a crossing is under way, or hidden code with an await still ahead of it (the hoisted `DisposeAsync` of `await using`/`await foreach`) | Step again with the user's kind (a step out continues as a step over), marked as crossing. Hidden code past its await's resume point is where a step out of an async method ends and stands. |
| `STEP_RETURN` after a skip, back into the statement the user's step started from | Step again with the user's kind: the returned-to offset is only mapped approximately, so the rest of the statement is covered by stepping it again. |
| Otherwise | `OnStopped(StopReason.Step)` with the current `SourceLocation`. |

Re-steps go through `CreateStepper` and `ContinueProcess` without reporting anything.

### Breakpoints during a step

`BreakpointHandler` checks the stepper before anything else: if the stepper is no longer active the
step has completed at the breakpoint's location and its `StepComplete` callback is queued behind the
breakpoint — the breakpoint is continued and the step reports the stop. If the stepper is still
active the breakpoint was hit before the step destination (inside a stepped-over call) and wins: the
step is cancelled and the breakpoint stop is reported — the cancellation happens after the hit-count
check, so a hit that does not stop leaves the step alone ([breakpoints.md](breakpoints.md)). A
breakpoint or entry stop, `Pause`, `Debugger.Break()` and *taken* exception stops cancel everything
(`Disable`, including the async notification breakpoint below); an exception the subscriber's filters
let run on (it calls `Continue`) leaves the step in flight - a step over an await whose task faults, or
over a call that throws and catches internally, still completes.

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
  └─ a breakpoint at EVERY yield point; status Yield   → plain stepper ALSO created
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

"Same invocation" means the same task on the method builder (the state machine box, which
`AsyncTaskMethodBuilder<T>` and friends keep in `m_task`). The id is read at the yield point and kept
in a strong handle, then compared by address at the resume point; an address of zero on either side
is accepted as a match, and only when the id cannot be read does a matching thread id stand in. The
field is read directly, without a func eval, whenever the task exists — at every resume, and at a
yield past the method's first: a breakpoint hit while an eval runs is continued through, so another
invocation of the same method resuming past the shared resume breakpoint during one would lose the
step. Only at a first yield, where the task does not exist yet, is it created through the builder's
`ObjectIdForDebugger` property, which the method keeps as its box from then on. A matching thread id alone proves nothing — the pool
reuses threads, so another invocation of the same method can resume on the stepping thread. The
yield breakpoint is only honoured on the thread that set the step up, which is safe there: until
the stepped invocation yields, it occupies that thread itself.

Every await of the method gets a yield breakpoint, not just the next one in IL order: control flow
decides which await runs next — a `break` inside an `await foreach` jumps over the loop's
`MoveNextAsync` await straight to the hidden `DisposeAsync` one — and the yield breakpoint that is hit
is the one carrying the step. A step resumed mid-flight by `StepController` (after a skipped method or
a hidden-region crossing) arms the same carry again through `ArmAwaitCarry`, which runs no evaluation
and is therefore safe inside a callback.

### Stepping out of an async method

Stepping out has to wait for the method's *task* to complete, not for `MoveNext` to return. The
builder (the `<>t__builder` field of the state machine `this`) is asked to
`SetNotificationForWaitCompletion(true)` — a func eval of the *instance* overload, picked by its
attributes: the builder also has a static `(bool, ref Task<T>)` overload, and starting the eval
against it with the instance arguments wedges the debuggee forever — which makes the runtime call
`Task.NotifyDebuggerOfWaitCompletion` when the awaiting code resumes; a breakpoint at IL offset 0 of
that method (in `System.Private.CoreLib`) catches it, and the handler turns the hit into an ordinary
step out, which lands in the resumed caller. The `ValueTask` builders declare no notification
method at all: there the builder's `m_task` field (the state machine box, present once the method
has yielded) takes the call on its non-generic `Task` base instead. `AsyncVoidMethodBuilder` has no
task to wait for, so `async void` methods get a plain step out; if any part of the setup fails the
plain step out is used as well.

## Cleanup

`StepController.Disable` (on every stop the user sees — breakpoint, entry, pause, `Debugger.Break()`,
a taken exception — and on disposal) deactivates the stepper, drops the async step with its
yield/resume breakpoints and handle, and the notification breakpoint: an async step out whose task
has not completed when the user stops elsewhere would otherwise fire a step stop later, in a place
unrelated to what they were doing. `CancelStep` alone (the plain stepper) is what a breakpoint that
merely evaluates — a false condition, a logpoint — uses, so an async step out survives those.
`AsyncStepper.ClearActiveStep` alone runs on every `StepComplete`.
