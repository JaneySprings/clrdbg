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
| `Status` | `Pending` (no debuggee yet), `NotProcessed` (debuggee running, no module resolved it yet), `NoSymbols` (no loaded module with symbols covers the document), `NoMatchingFunctions`, `Bound`, `Error` (+ `Error` text). `Verified` is `Status == Bound`. |
| `Location` | The bound `SourceLocation`: document path as in the PDB, span, checksum, Source Link URL. |
| `HitCount` | Incremented on every hit, before the hit condition is checked. |
| `CorBreakpoint` / `FunctionBindings` (internal) | The `ICorDebugFunctionBreakpoint`(s) behind it — one for a source breakpoint, one per matching method for a function breakpoint. |

The status is the engine's statement of fact; the adapter turns it into vsdbg's messages
("The breakpoint is pending and will be resolved when debugging starts.", "Breakpoint has not been
processed by the debugger.", …) in `Resources`.

## Setting and binding

`SetBreakpoints(file, requests)` deactivates and drops the file's previous breakpoints and creates the
new ones; `SetFunctionBreakpoints` does the same for all function breakpoints. Without a process they
stay `Pending`; with one, binding is attempted immediately, and again whenever a module loads
(`BindPending(module)`), which is how breakpoints set before the launch bind as the debuggee starts.
When the attach lands every breakpoint is reported once more (`Pending` → `NotProcessed`) so the
client shows them unverified until their module arrives.

Binding a source breakpoint (`TryBind`) asks each module with symbols to resolve it:

1. `ModuleMetadataReader.FindDocument` looks the document up by full path (case-insensitive, `\`
   normalized to `/`), falling back to a file-name match for PDBs built from another location.
2. `SequencePointResolver.Resolve` chooses the sequence point (below).
3. `ICorDebugFunction.GetILCode().CreateBreakpoint(ilOffset)` + `Activate(true)`; the breakpoint
   takes the resolved span and `Location`, and `OnBreakpointChanged` reports it.

A module that fails with an exception marks the breakpoint `Error`; when no loaded module contains
the document the breakpoint stays `NoSymbols` until a later module does.

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
*every* matching method at its first non-hidden sequence point; a breakpoint therefore accumulates
`FunctionBindings` across modules and overloads, becomes `Bound` with the first one, and reports
`NoMatchingFunctions` when the process is running and nothing matched anywhere.

## When a breakpoint is hit

`BreakpointHandler.HandleBreakpointAsync` runs these checks in order, each either continuing the
debuggee or moving on:

1. A function evaluation is running → continue (the hit belongs to evaluated code).
2. A step is in progress → if the step is already complete, continue: the `StepComplete` callback is
   queued right behind this one and reports the stop, so the breakpoint at the step destination is
   not reported twice. Otherwise the breakpoint came first (inside a stepped-over call) and cancels
   the step.
3. Not an `ICorDebugFunctionBreakpoint` → continue.
4. The `AsyncStepper` gets a look: its yield/resume breakpoints continue, its
   `NotifyDebuggerOfWaitCompletion` one turns into a step out ([stepping.md](stepping.md)).
5. The `stopAtEntry` breakpoint (matched by identity, or by not being any known breakpoint) →
   `OnStopped(StopReason.Entry)`.
6. Unknown breakpoint → continue.
7. `HitCount++`, then the hit condition: `3` / `== 3` (exactly), `>= 3`, `> 3`, `<= 3`, `< 3`,
   `% 3` (every third hit); unparsable conditions never stop.
8. The condition, evaluated in the top frame of the hitting thread
   ([evaluation.md](evaluation.md)); a compile or runtime error counts as "not met".
9. A log message: every `{expression}` is evaluated and replaced by its display value (left as-is
   when it fails), `OnLogPoint` receives the text and the debuggee continues.
10. `OnStopped(StopReason.Breakpoint)` with the breakpoint's `Location` (the resolved one for source
    breakpoints, the current frame's for function breakpoints) and `[breakpoint.Id]`.

## Entry point

With `LaunchInfo.StopAtEntry`, `ModuleHandler.TrySetEntryPointBreakpoint` places a one-shot
`ICorDebugFunctionBreakpoint` on the first loaded assembly that has a managed entry point
(`CorHeader.EntryPointToken`, a MethodDef), at the entry method's first sequence point. It is not
tracked by the manager: `TryHandleEntryPointBreakpoint` recognizes it, deactivates it and reports
`StopReason.Entry`.

## Deactivation

Replacing, clearing or disposing calls `TryActivate(false)` on every `ICorDebugFunctionBreakpoint`
involved; `CORDBG_E_PROCESS_TERMINATED` ends the loop quietly (the process is gone) and any other
failure is only logged.
