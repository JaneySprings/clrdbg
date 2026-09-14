# Breakpoints

`Breakpoints/BreakpointManager` owns every breakpoint the client requested and binds them to the
debuggee's code; `Handlers/BreakpointHandler` decides what a hit means. Source positions are mapped
to IL offsets by `Metadata/SequencePointResolver`, function names by
`Breakpoints/FunctionBreakpointPattern` and `FunctionBreakpointResolver`.

## The model

A `Breakpoint` is created from a `BreakpointRequest` (line, optional column, condition, hit
condition, log message) or a `FunctionBreakpointRequest` (name, condition, hit condition) and keeps:

| Member | Meaning |
|---|---|
| `Id` | Session-unique, increasing. Replacing a file's breakpoints issues new ids. |
| `FilePath` / `FunctionName` | One of the two is set; `IsFunctionBreakpoint` tells them apart. |
| `Line`, `Column`, `EndLine`, `EndColumn` | The requested line until bound, the resolved statement span afterwards. |
| `Status` | `Unbound` (no loaded module with symbols contains the location — or, for a function breakpoint, has a matching function — yet), `SourceMismatch` (a module has an equally named document whose content differs from the local file), `Bound`, `Error` (+ `Error` text). `Verified` is `Status == Bound`. |
| `Location` | The bound `SourceLocation`: document path as in the PDB, span, Source Link URL — for a function breakpoint that of its first binding, which is what it reports. |
| `HitCount` | Incremented on every hit, before the hit condition is checked. |
| `Bindings` (internal) | The `BreakpointBinding`s behind it (runtime breakpoint, module, method, IL offset) — one for a source breakpoint, one per matching method for a function breakpoint. Breakpoints resolving to one location share a single runtime breakpoint, so the runtime reports the location once for all of them. `Line` is the bound line, the requested one until then. |

The status is the engine's statement of fact; the adapter turns it into a message the client shows
with the unverified breakpoint (`Resources`, `DebuggerExtensions.ToStatusMessage`).

## Setting and binding

`SetBreakpoints(file, requests)` deactivates and drops the file's previous breakpoints and creates the
new ones; `SetFunctionBreakpoints` does the same for all function breakpoints. Binding is attempted
against the loaded modules at once (none before the launch, the breakpoint stays `Unbound`), and again
whenever a module loads (`BindPending(module)`), which is how breakpoints set before the launch bind as
the debuggee starts.

Binding a source breakpoint (`TryBind`) asks each module with symbols to resolve it:

1. `ModuleMetadataReader.FindDocument` looks the document up by full path (case-insensitive, `\`
   normalized to `/`), falling back to a file-name match for PDBs built from another location. A
   file-name match is verified against the PDB's document hash: one whose content matches counts as
   exact, one that differs is rejected with `ManagedDebugger.RequireExactSource` on (the default) and
   makes the breakpoint `SourceMismatch` when nothing else resolves it. A binding made by file name
   alone is later moved to a module whose document matches exactly (`TryRebind`).
2. `SequencePointResolver.Resolve` chooses the sequence point (below). The debugger attributes
   (`[DebuggerHidden]`, `[DebuggerStepThrough]`, `[DebuggerNonUserCode]`) steer stepping and exception
   stops, not breakpoints: a method carrying one takes a breakpoint like any other.
3. `CreateBinding`: the runtime breakpoint another breakpoint already holds at that module, method
   and IL offset (a function breakpoint on the method, another breakpoint on the statement), or a new
   `ICorDebugFunction.GetILCode().CreateBreakpoint(ilOffset)` + `Activate(true)`; the breakpoint takes
   the resolved `Location`, and `OnBreakpointChanged` reports it.

A module that fails with an exception marks the breakpoint `Error`; when no loaded module contains
the document the breakpoint stays `Unbound` until a later module does.

### Choosing the sequence point

For the requested position every method of the document is examined through its non-hidden
sequence points that end at or after the position, collecting per method:

- `First` — the point with the smallest end (the nearest statement at or after the position);
- `Last` — the point with the largest end;
- `Covering` — among the points whose span contains the position, the one starting latest.

Then:

| Situation | Choice |
|---|---|
| No method covers the position (blank line, comment, closing brace) | The `First` point with the earliest start across methods — the breakpoint snaps to the next line with code, and the client sees the adjusted line. |
| A whole line covered only by statements starting above it, and another method has a statement at or below the line inside one of them (a blank line in the body of a multi-line lambda: the delegate assignment's point spans the whole lambda text) | That method's `First` point — the lambda's next statement, where the user put the breakpoint, rather than up to the start of the spanning statement. |
| Exactly one method covers it | Its `Covering` point. |
| Several cover it, one `Covering` starts later than the others | That one — the innermost lambda, whose own statement starts after the enclosing statement (e.g. the delegate assignment) that spans it. |
| Several `Covering` points start at the same position (`items.Select(i => i * 2)` on one line) | netcoredbg's containment rule: if the nested method's range lies inside the outer's first statement the call site wins; otherwise, if the outer's first statement ends after the nested's, the lambda body wins. |

With a column the comparisons use (line, column) positions; without one, whole lines.

## Function breakpoints

`FunctionBreakpointPattern.Parse` accepts what a user types in the breakpoints view:

| Input | Type pattern | Method | Arity | Parameters |
|---|---|---|---|---|
| `Main` | any | `Main` | any | any |
| `Program.Main` | `Program` (suffix match: `My.App.Program` matches) | `Main` | any | any |
| `Repository<T>.Find<TKey, TValue>` | ``Repository`1`` | `Find` | 2 | any |
| `Program.Add(int, System.String, List<int?>)` | `Program` | `Add` | any | ``System.Int32, System.String, List`1<System.Nullable`1<System.Int32>>`` |

C# keywords become their metadata names, generic types carry their arity, `?` becomes
``System.Nullable`1<…>``; parameters match by metadata signature name with a namespace-suffix rule
(``List`1<…>`` matches ``System.Collections.Generic.List`1<…>``). Malformed input (`Main(`, `List<int.Add`,
an empty parameter) is an `ArgumentException` and becomes `BreakpointStatus.Error` with that message.

`FunctionBreakpointResolver.Resolve` walks every type definition of a module with symbols and binds
*every* matching method at its first non-hidden sequence point (`ResolveMethodEntry`: for an async or
iterator method, which has no sequence points of its own, that of its state machine's `MoveNext`, found
through the PDB's kickoff-method link); the parameter list normalizes `ref`/`out`/`in`, arrays and
pointers to their signature names (`System.Int32&`, `System.String[]`); a breakpoint therefore accumulates
`Bindings` across modules and overloads, becomes `Bound` with the first one, and stays `Unbound`
while nothing matched anywhere.

## When a breakpoint is hit

`BreakpointHandler.HandleBreakpointAsync` runs these checks in order, each either continuing the
debuggee or moving on:

1. A function evaluation is running → continue (the hit belongs to evaluated code).
2. A step is in progress and already complete, and the breakpoint was hit by the stepping thread →
   continue: the `StepComplete` callback is queued right behind this one and reports the stop, so the
   breakpoint at the step destination is not reported twice. Another thread's breakpoint is never
   the step destination, it goes on to be reported as its own stop. A step still in flight is left
   alone here — see 8.
3. Not an `ICorDebugFunctionBreakpoint` → continue.
4. The `AsyncStepper` gets a look: its yield/resume breakpoints continue, its
   `NotifyDebuggerOfWaitCompletion` one turns into a step out ([stepping.md](stepping.md)).
5. The `stopAtEntry` breakpoint (matched by identity, or by not being any known breakpoint) →
   every step is disabled, `OnStopped(StopReason.Entry)`.
6. Unknown breakpoint → continue. Otherwise every breakpoint sharing the runtime breakpoint is hit
   at once (`FindByCorBreakpoint`), and 7–10 run for each of them.
7. `HitCount++`, then the hit condition: `3` / `== 3` (exactly), `>= 3`, `> 3`, `<= 3`, `< 3`,
   `% 3` (every third hit); unparsable conditions never stop — and a hit that does not stop leaves an
   in-flight step alone, the step carries on past the breakpoint.
8. From here the breakpoint either stops or evaluates in the debuggee. A stop wins over a step in
   flight (11). A step survives an evaluation that does not stop: on the stepping thread itself the
   stepper is suspended first (`SuspendStep` — the hijacked evaluation and the stepper's patches would
   share the thread) and re-armed afterwards (`ResumeSuspendedStep`: a thread left deeper than the
   statement the step started in, inside a stepped-over call, steps out marked as a skip and the rest
   of the statement is stepped from there; at the step's own depth the user's kind goes on); another
   thread's stepper stays armed, and a completion arriving while the evaluation runs is held back —
   the completed thread is frozen (`THREAD_SUSPEND`) so it does not run on with the debuggee — and
   reported once the breakpoints have decided not to stop (`ContinueAfterEvaluation`). An async step
   out, which has no stepper, keeps waiting through 9 and 10.
9. The condition, evaluated in the top frame of the hitting thread
   ([evaluation.md](evaluation.md)). One that cannot be evaluated — a name that does not exist, a call
   that throws, a timeout, a result that is no `bool` — *stops*, the way a breakpoint without a
   condition would, and the stop carries a `FailedCondition` (the breakpoint and the reason: the
   compiler's error, "X was thrown", "the result is not a boolean") for the host to show — the adapter
   prints "The breakpoint condition '…' could not be evaluated: <reason>" with the location to the
   debug output before the stop: passed silently, a breakpoint the user asked for would go missing
   with nothing to say why.
10. A log message: every `{expression}` is evaluated and replaced by its display value (left as-is
    when it fails), `OnLogPoint` receives the text and the debuggee continues — through
    `ContinueAfterEvaluation`, like a condition that is not met.
11. When at least one breakpoint stops: every step is disabled (`StepController.Disable`, the async
    notification breakpoint included), then `OnStopped(StopReason.Breakpoint)` with the first stopping
    breakpoint's `Location` (the resolved one for source breakpoints, the current frame's for function
    breakpoints) and the ids of all the breakpoints that stop. When none does, the debuggee continues
    — through `ContinueAfterEvaluation` when anything was evaluated.

## Entry point

With `LaunchRequest.StopAtEntry`, `ModuleHandler.TrySetEntryPointBreakpoint` places a one-shot
`ICorDebugFunctionBreakpoint` on the first loaded assembly that has a managed entry point
(`CorHeader.EntryPointToken`, a MethodDef), at the entry method's first sequence point - or, for the
compiler's `<Main>` bridge over an async `Main`, at the first statement of the `Main` behind it
(`ResolveEntryPoint`). It is not
tracked by the manager: `TryHandleEntryPointBreakpoint` recognizes it, deactivates it and reports
`StopReason.Entry`.

## Deactivation

Replacing, clearing or disposing calls `TryActivate(false)` on every `ICorDebugFunctionBreakpoint`
involved that no other breakpoint still shares (`Deactivate`); `CORDBG_E_PROCESS_TERMINATED` ends the
loop quietly (the process is gone) and any other failure is only logged.
