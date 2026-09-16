# Expression evaluation

`Evaluation/` evaluates C# expressions without a managed-code interpreter in the debuggee: Roslyn's
own expression compiler turns the expression into a tiny assembly, and `CilInterpreter` executes
that assembly's IL on the debugger side, reaching into the debuggee through `ICorDebug` for every
read, write, allocation and call.

```
"items.Count * 2"
   │  ExpressionCompiler (Roslyn ExpressionCompiler against the loaded modules' metadata)
   ▼
CompiledExpression: in-memory PE with <>x.<>m0(...)  ── cached per (method, IL reuse span, expression)
   │  CilInterpreter  (+ EvaluationMetadataResolver for tokens, FuncEvalRunner for the debuggee)
   ▼
ICorDebugValue  ── wrapped in an EvaluationResult that owns the handle keeping it alive
```

The same path serves the `evaluate` request, breakpoint conditions, logpoint placeholders,
`DebuggerDisplay`/`ToString()` templates and `$exception`.

## Compiling: `ExpressionCompiler`

The Roslyn expression compiler (rebuilt from the Roslyn sources in the
[`DotNet.Debugging.Evaluation`](../evaluation/README.md) project and driven through its
`ExpressionContext`) compiles against **metadata blocks** — the raw metadata of the debuggee's loaded
modules, obtained through `IMetaDataTables2.GetMetaDataStorage()`. When the same
assembly identity is loaded more than once (several `AssemblyLoadContext`s) only one module per
identity is passed, preferring the one the evaluation binds against, so the tokens emitted into the
expression assembly refer to the instance the user is debugging.

Two contexts exist:

- **Method context** (frame evaluations): the frame's method is located in the compilation
  (`GetSourceMethod`/`GetMethod` by module id and token), and `MethodDebugInfo.ReadFromPortable`
  reads the PDB at the normalized IL offset for the local names, the hoisted locals in scope, the
  local constants, imports and the *reuse span* (the IL range the compiled expression stays valid
  for). Locals come out as `LocalSymbol`s in slot order, which is what lets the generated method read
  the frame's actual locals.
- **Type context** (`DebuggerDisplay` fragments): a synthesized method on the displayed object's type,
  with the object as the only argument (`<>x.<>m0(T <>4__this)`); each fragment of a display string
  is one such expression, its format specifier (`,nq`) split off by `DebuggerDisplayTemplate` before
  ([variables.md](variables.md)).

When the thread has a current exception, a `$exception` alias is registered so the expression can name
it. The compiler also needs `Microsoft.VisualStudio.Debugger.Clr.IntrinsicMethods` — the debugger
intrinsics it emits calls to for synthetic variables and aliases — which `DotNet.Debugging.Evaluation`
compiles once into a small in-memory assembly (`IntrinsicMethodsReference`).

Compilation errors come back as the diagnostic messages joined with `; `. When the errors blame an
assembly the debuggee has not loaded — an unknown type's, or `System.Linq` for an extension method
that could be one of its, found the way Roslyn's own compiler does (`GetMissingAssemblyIdentities`),
plus the shim's own guess for the two errors Roslyn's retry does not cover: an unknown `Enumerable` or
`Queryable` name means `System.Linq`, an unknown member of a `System.*` namespace (`System.Linq.Enumerable`,
`System.Text.Json.JsonSerializer`) the assembly named like the namespace, a wrong guess failing to load
and leaving the error standing —
`ExpressionEvaluator` loads it into the debuggee (`Assembly.Load`, a module event follows) and compiles
again, once per assembly; a program that never used LINQ can still evaluate `strings.Where(...)` or
`Enumerable.Range(1, 3)`. Results are cached in an
LRU of 256 entries keyed by context kind, module, method token, whether an exception is present and
the text; a method-context entry also records the `ReuseConstraints` Roslyn computed
(the IL span in which the same locals are in scope) and serves every IL offset inside it, so stepping
through a scope does not recompile the watches. The cache and the metadata blocks are dropped
whenever a module loads (`ManagedDebugger.ModulesVersion`).

## Executing: `CilInterpreter`

`CompiledExpression` decodes the generated method's IL once (`CilInstructionDecoder`: opcode table
from `System.Reflection.Emit.OpCodes`, operands and branch targets resolved to instruction indexes).
`InterpretAsync` then runs a classic evaluation stack of `CilValue`s:

| `CilValue` holds | Used for |
|---|---|
| `Value` — a host primitive, string or `ResolvedCilType` | Constants, arithmetic results, `ldtoken`, interpolated-string builders. |
| `Value` — a `HostObject`, `HostDelegate`, `HostFunction`, `HostSequence` or `HostSpan` (`HostValues.cs`) | What only exists in the debugger: an instance of a type the expression assembly declares (a closure, a display class, an anonymous type), a delegate the expression created, the function `ldftn` pushed, a sequence a System.Linq operator computed here, a span the expression built. See *Code the expression declares* and *Spans* below. |
| `CorValue` — a debuggee `ICorDebugValue` | Everything read from the debuggee; reference values are pinned with strong handles (`EvaluationHandleScope.Root`) so they survive later func evals. |
| `Location` — an `ICilLocation` | Addresses: a debuggee slot (`CorDebugLocation`: local, argument, field, element), a host temporary (`TemporaryLocation`), a synthetic variable (`SyntheticVariableLocation`, a one-element array allocated in the debuggee) or a slot the runtime cannot read at this instruction (`UnavailableLocation`, optimized away - reading it fails with a message saying so). A by-reference slot (a `ref` parameter or local, the `this` of a struct method) reads as the location it points to, which the IL then dereferences with `ldind`/`ldobj`. |

The method's arguments are the frame's arguments (or the root object), its first locals are the
frame's locals — so `x = 5` in the evaluate window writes the real local — and the remaining slots
are temporaries. A frame slot is fetched from a fresh frame on every access (`CorDebugLocation` with
a fetch): the frame does not survive a func eval, and the runtime's value object of a value-typed
slot is a snapshot taken when it is obtained — an instance call on the slot (`maybe = 8` is a
`Nullable<int>` constructor call on the local's address) changes the debuggee's memory behind it,
which only a fresh fetch shows. The frame's generic arguments are split into the declaring type's
and the method's by the type's arity, for `!0`/`!!0` resolution.

| Opcode family | Execution |
|---|---|
| `ldc.*`, `ldnull`, `ldstr`, `ldtoken` | Host constants (`ldtoken` pushes the resolved type, or the data field of an array initializer). |
| `ldftn`, `ldvirtftn` | A `HostFunction` naming the method; the delegate constructor that follows turns it into a `HostDelegate`. |
| `ldarg*`, `ldloc*`, `starg`, `stloc`, `ldarga`, `ldloca` | Read/write through the locations; `ld*` of references roots them. |
| `add … shr.un`, `neg`, `not`, `ceq … clt.un`, `conv.*` | On the host, with int32/int64/float promotion, overflow-checked and unsigned variants, NaN-aware comparisons; enum and small struct values read as integers (an enum through its `value__` field, so the underlying type's sign holds). Host values are written back with wrapping bit reinterpretation (`CilValueEncoding`): an unsigned slot holds its signed twin on the stack, a comparison result stores into a `bool`, and a native integer takes the debuggee's pointer size. |
| `br*`, `beq … ble.un`, `switch` | Branches by instruction index. The ordered comparisons (`bge`, `ble`, …) never take a NaN operand, their `.un` forms always do (ECMA-335 III.3); for integers `.un` means unsigned. |
| `ldind/stind/ldobj/stobj/cpobj/initobj` | Through locations; `initobj` creates a default value (zero, null, or a debuggee struct instance). |
| `newarr`, `ldlen`, `ldelem*`, `stelem*`, `ldelema` | `NewParameterizedArray` for primitive and reference element types; `Array.CreateInstance(Type, int)` in the debuggee for other structs (`DateTime[]`), as `ICorDebugEval` cannot allocate those. |
| `isinst`, `castclass` | `Type.GetType(assemblyQualifiedName)` + `Type.IsInstanceOfType(value)` evaluated in the debuggee. |
| `box`, `unbox`, `unbox.any` | Boxes are allocated with `NewParameterizedObjectNoConstructor` and filled byte-wise; unboxing checks the exact class (the object of a boxed primitive is a VALUETYPE of `System.Int32` and friends) and reads a primitive into a host value, so the arithmetic that follows sees an `int`, not a debuggee value. `unbox.any Nullable<T>` builds the nullable: an empty one for null, one holding the boxed `T` otherwise; `box Nullable<T>` boxes the value, or yields null when there is none, the way the runtime does. |
| `ldfld/ldflda/stfld`, `ldsfld/ldsflda/stsfld` | `GetFieldValue`, `GetStaticFieldValueAsync` (runs the static constructor on demand); the fields of a type the expression assembly declares are host locations (a host object's, or `EvaluationState`'s statics after the type's `.cctor`). |
| `newobj` | `NewParameterizedObject` with the constructor's declaring-type arguments. A type of the expression assembly becomes a host object whose constructor is interpreted; a delegate over a `HostFunction` a `HostDelegate`; a multidimensional array type goes through `Array.CreateInstance`; the common `string` constructors are built on the host; a `Span<T>`/`ReadOnlySpan<T>` constructor yields a `HostSpan`. |
| `call`, `callvirt` (+ `constrained.`) | See below. |
| `ret` | The result; `nop`/`break` are skipped. |

Anything else (`throw`, exception blocks, `calli`, pointer arithmetic, `localloc`, …) is a
`NotSupportedException` naming the opcode and IL offset; other failures are wrapped with the offset
and opcode as well.

**Code the expression declares** runs in the debugger. Roslyn lowers lambdas, closures and anonymous
types into classes of the expression assembly (`<>c`, `<>c__DisplayClass0_0`, `<>f__AnonymousType0<…>`),
which the debuggee never loads: the interpreter instantiates them as host objects (`HostObject`, the
fields held as temporary locations; the statics and the `.cctor` of a closure class live for the
evaluation in `EvaluationState`), `ldftn` pushes a `HostFunction`, and a delegate constructor over one
yields a `HostDelegate` — also for a method group over a debuggee method, which has no function pointer
on this side. The interpreter invokes such a delegate itself, on `Invoke` or per element inside a
System.Linq operator. A host value cannot leave the debugger: a lambda handed to a debuggee method
(`wrapper.Map(v => v + 1)`) or an anonymous object as the result is reported as an error, while an
anonymous object's members evaluate (`new { Name = "x" }.Name`). Inside a method of a host object the
`!0`/`!!0` parameters mean the object's own instantiation, and the resolver's token caches are bypassed.

**System.Linq with a lambda** (`LinqEmulator`). An `Enumerable` operator handed a host delegate, or
whose source is a sequence the debugger computed, runs on the host: the source is enumerated (an array's
elements directly, anything else through a func eval of `Enumerable.ToArray<T>`), the lambda is
interpreted per element, and a sequence result is a `HostSequence` that later operators consume and
that becomes a debuggee array of its element type once it reaches the debuggee or the result —
`words.Where(w => w.Length > 4)` shows as a `string[]`, `ToList()` builds a `List<T>` through its
`IEnumerable<T>` constructor. Supported: `Where`, `Select` (with and without the index), `SelectMany`,
`Any`, `All`, `Count`, `LongCount`, `First`/`Last`/`Single` and their `OrDefault` forms, `ElementAt(OrDefault)`,
`Sum`, `Min`, `Max`, `Average` (int, long, float and double), `Aggregate`, `OrderBy(Descending)`,
`ThenBy(Descending)`, `Skip`, `Take`, `SkipWhile`, `TakeWhile`, `Distinct`, `Reverse`, `Contains`, `Concat`,
`ToList`, `ToArray`. Ordering and equality follow `Comparer<T>.Default`: strings by the current culture,
numbers by value (boxed ones unboxed first; an unsigned key compares unsigned, decided by the key's static
type since the interpreter holds a computed `uint` as its signed twin), references by identity. `Min` and
`Max` follow `Enumerable`'s NaN rules — the minimum is NaN as soon as one is met, the maximum ignores NaN
unless nothing else is there. An empty `First()` reports `InvalidOperationException` the way
the debuggee would. An operator without a lambda over a debuggee source keeps running in the debuggee.

**Array initializers, multidimensional arrays, string constructors.** `new[] { 1, 2, 3 }` is `ldtoken`
of a data field of the expression assembly followed by `RuntimeHelpers.InitializeArray`: the field's
bytes (`GetEvaluationFieldData`) are copied into the elements. `new int[2, 3]` is a `newobj` on the array
type's own constructor, served through the debuggee's `Array.CreateInstance(Type, int[])` because
`ICorDebugEval` allocates single-dimensional arrays only. `new string(char, int)` and the `char[]`
constructors are built on the host, the runtime refusing them in a func eval.

**Spans** (`SpanEmulator`). A `Span<T>`/`ReadOnlySpan<T>` is a byref-like struct no func eval can take or
return, and the compiler lowers more to it than the user writes: `text + letter` becomes
`String.op_Implicit(string)`, `new ReadOnlySpan<char>(in letter)` and `String.Concat(ReadOnlySpan<char>, …)`;
an array literal handed to a span API (`new[] { 3, 1, 2 }.Contains(1)`, which C# 14's first-class spans
bind to `MemoryExtensions`) is `ldtoken` of a data field followed by `RuntimeHelpers.CreateSpan<T>`. The
spans the expression builds are `HostSpan`s — over a string, over a debuggee array (kept by its source
reference and sliced by index; the elements are read and written through the array's own slots, so
`numbers.AsSpan()[0] = 5` reaches the debuggee), or over the values or variables the span was created
over — and the span members (`Length`, `IsEmpty`, the indexer as a location, `Slice`, `ToString`,
`ToArray`, `op_Implicit`, `Empty`), the string concatenations and the `MemoryExtensions` operators
(`AsSpan`, `Contains`, `IndexOf`, `LastIndexOf`, `SequenceEqual`, `StartsWith`, `EndsWith`) run on the
host; `span.ToString()` arrives as a `constrained.` call to `Object.ToString` and is routed the same way.
A span of the debuggee (a frame local) answers `Length` from its `_length` field only, its elements sit
behind a managed pointer the debugger does not follow; a host span cannot be handed to a debuggee method.

**Syntax the evaluator does not support** (`Handlers/EvaluationSyntaxTests.UnsupportedSyntaxIsReportedTest`
keeps the list, each form is reported as an error):

- a lambda handed to a debuggee method (`wrapper.Map(v => v + 1)`, `person.Convert(p => p.Name)`): the
  delegate only exists in the debugger. Interpreting the debuggee method's own IL would lift this;
- an anonymous object as the result or as a debuggee argument (`new { Name = "x" }`, its `ToString()`):
  the type only exists in the debugger;
- variables declared by patterns (`boxed is int n ? n + 1 : 0`, `count is var any`): Roslyn's
  expression compiler turns declared locals into pseudo-variables (`CreateVariable`/`GetVariableAddress`)
  by rewriting `BoundLocal` references, which a pattern's declaration is not — its code generator then
  fails on the undeclared local (a `KeyNotFoundException`, which `ExpressionEvaluator` reports as
  "Variables declared by a pattern … are not supported in the debugger"). `out var` works.

Assignments to a slot of a reference type (`CorDebugLocation.IsReferenceSlot`: an `object`, class, string
or array local, field or element) take the source reference, or null, whatever the slot holds
(`boxed = 2.5` on an `object` local holding a boxed `int` stores a new box - writing into the old one
would change every other reference to it); a host primitive going into such a slot is boxed first. Values
copy their bytes into a value slot (an enum or struct the evaluation produced is a box, read through). A constant called through
`constrained.` (`Options.C.ToString()`) is boxed as the constrained type, not as its underlying
integer. Constants fold unchecked (`(sbyte)200` is `-56`), the way the compiler options of the
expression compiler have it.

**Calls.** A call target is one of:

- a *debugger intrinsic* — `CreateVariable`/`GetVariableAddress`/`GetObjectByAlias` implement the
  synthetic variables a declaration expression creates (stored in debuggee-allocated arrays),
  `GetException` the `$exception` alias;
- a method of the expression assembly itself (a lambda body, a local function, a closure's or anonymous
  type's member), interpreted with temporary locals — an instance method of a host object under the
  object's generic instantiation (`EvaluationMetadataResolver.EnterGenericContext`); a type's static
  constructor runs before its statics are first touched (`EvaluationState`);
- `Invoke` on a delegate the expression created (`HostDelegate`): the lambda is interpreted, a method
  group's debuggee method func-evaled;
- `System.Object..ctor` on a host object (nothing to do) and `RuntimeHelpers.InitializeArray` over a data
  field of the expression assembly (the bytes are copied into the array's elements);
- a `System.Linq.Enumerable` operator handed a host delegate or a host sequence, run by `LinqEmulator`;
- a span member, a `String` concatenation of spans, a `MemoryExtensions` operator or `RuntimeHelpers.CreateSpan`,
  run by `SpanEmulator` (see *Spans*);
- `System.Type.GetTypeFromHandle`, answered with the `System.Type` of the token (`typeof`);
- the `DefaultInterpolatedStringHandler` calls interpolated strings are lowered to, emulated with a
  host `StringBuilder` — debuggee values are formatted by calling `Object.ToString()` on them in the
  debuggee (dispatched virtually by the func eval; unlike `String.Concat(object)` it survives a trimmed
  core library), host values with `IFormattable`;
- a runtime method: the arguments are *materialized* into debuggee values (strings created with
  `NewString`, primitives with `CreateValue` - except `nint`/`nuint`, which `ICorDebugEval` cannot
  create and which are built as the `IntPtr` struct instead -, by-ref arguments passed as their debuggee slot or a
  temporary copied back afterwards), the receiver boxed when it is a value type (honouring
  `constrained.`), the declaring type's arguments taken from the receiver's exact type, and
  `FuncEvalRunner.CallFunctionAsync` runs it with `throwOnException`. A by-ref return becomes a
  location.

**Resolution.** `EvaluationMetadataResolver` maps the expression assembly's tokens to the debuggee:
`MemberRef`/`MethodSpec`/`TypeRef`/`TypeSpec` handles are resolved to a `ModuleInfo` and a definition
handle by assembly identity (name, version, culture, public key or its SHA1 token), preferring the
module the expression was compiled against and falling back to the simple name for redirected
versions; generic arity and signatures are compared by metadata type names. `ResolvedCilType`
describes a type as a primitive, a runtime type (module, handle, type arguments) or an array, and
`GetCorDebugType` materializes it with `ICorDebugClass2.GetParameterizedType` /
`ICorDebugAppDomain2.GetArrayOrPointerType`.

**Result.** The top of the stack is materialized into an `ICorDebugValue` of the method's return type
(`MaterializeAsync`: host primitives become `CreateValue`d generics, strings are created in the
debuggee, struct results are written into a fresh instance). The `EvaluationResult` owns the strong
handle rooting a reference result; `ManagedDebugger.EvaluateAsync` keeps it when the value is
expandable — it is then released with the variables references — and disposes it otherwise.

## Func evals: `FuncEvalRunner`

Every call into the debuggee is an `ICorDebugEval` on the evaluation thread:

```
eval.CallParameterizedFunction / NewParameterizedObject / NewParameterizedObjectNoConstructor /
eval.NewParameterizedArray / NewString
IsRunning = true; process.Continue()
await WaitForEvalEventAsync()       ── the engine keeps dispatching callbacks meanwhile
   5 s without completion          → eval.Abort(); 5 s more → ICorDebugEval2.RudeAbort()
                                      the wait goes on (it is dispatching callbacks), the completion
                                      the abort brings is released, EvaluationTimeoutException
EvalComplete   → eval.GetResult() (CORDBG_S_FUNC_EVAL_HAS_NO_RESULT for void)
EvalException  → the thrown exception object; with throwOnException an EvaluationThrewException
                 "Evaluation threw System.InvalidOperationException" (the type name kept on the
                 exception) and the handle released
IsRunning = false
```

The time-out is what keeps a blocking getter — `Console.ReadLine()`, a semaphore nothing releases, a
socket read — from holding the engine's lock, and with it every later request, for the rest of the
session: the wait runs on the thread that holds `syncLock`. The `ImplicitEvalBudgetMilliseconds` of
`VariableProvider` is a different thing — it decides whether to *start* another `ToString`/
`DebuggerDisplay` evaluation in a listing, never bounds one in flight.

**A refused start.** The runtime sets an evaluation up only on a thread stopped at a safe point in
managed code, at a breakpoint or a step. Every other stop it marks as `USER_UNSAFE_POINT` in the
thread's user state — the same `IsThreadAtSafePlace` check it makes before a func eval — so
`EnsureThreadCanEvaluate` reads the user state and refuses the eval *before starting it*, throwing an
`EvaluationRefusedException` (a thread stopped at an exception is exempt: the user state calls it
unsafe too, but the runtime sets an eval up from the exception's context instead of hijacking the
thread, `evalDuringException`, so `$exception` and its properties evaluate as before): `Cannot evaluate the expression: the thread is paused in a sleep, wait,
or join` when `USER_WAIT_SLEEP_JOIN` is also set, otherwise `... is stopped in native or optimized
code, where the runtime cannot run an evaluation` (an idle app blocked in a platform run loop such as
`UIApplicationMain`, a P/Invoke, or a GC-unsafe point in an optimized method). Checking the user
state, rather than reacting to what the start call returns, is what makes this work on the
mobile/maccatalyst remote host: there the start is *not* refused synchronously — the eval is accepted,
the process continued, and the eval simply never completes, so without the pre-check it is given up on
only after the 10s abort cycle and, because a thread that is not at a safe point cannot be made to run
the abort either, wedges the session (an eval started at a breakpoint is aborted fine there). The local runtime still refuses an unsafe start synchronously; `CreateRefusal`
turns the precise HRESULTs it gives for stops the user state did not catch (a stack overflow, a
prolog) into their own messages. Nothing runs on the thread until the debuggee moves on, which
`VariableProvider` relies on (see [variables.md](variables.md)).

`IsRunning` is what the breakpoint and exception handlers consult to continue through stops the
evaluated code produces. Two helpers sit on top: `GetStaticFieldValueAsync` (retrying after the
static constructor) and `GetPropertyValueAsync` (getter lookup along the base types, used for
exception details and the async stepper). Func evals neuter frames — callers re-obtain
`ICorDebugILFrame`s after every await.

`EvaluationHandleScope` tracks every handle produced during an interpretation (results of func evals,
`Root`ed references) and releases them when the interpretation ends, except the one detached as the
result.

## Errors

`ExpressionEvaluator.EvaluateAsync` never throws: compile errors, interpreter failures and debuggee
exceptions come back as `EvaluationResult.Error` (`error: …`). `ManagedDebugger.EvaluateAsync` turns
that into an `EvaluationException` for the request; conditions treat it as "not met", logpoints keep
the placeholder, and `DebuggerDisplay` failures show as error-marked variables.
