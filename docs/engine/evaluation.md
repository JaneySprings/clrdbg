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
- **Type context** (`DebuggerDisplay` templates): a synthesized method on the displayed object's type,
  with the object as the only argument; the template is parsed first and `{Name,nq}`-style alignment
  clauses removed (`RemoveFormatSpecifierRewriter`), as they are not valid C#.

When the thread has a current exception, a `$exception` alias is registered so the expression can name
it. The compiler also needs `Microsoft.VisualStudio.Debugger.Clr.IntrinsicMethods` — the debugger
intrinsics it emits calls to for synthetic variables and aliases — which `DotNet.Debugging.Evaluation`
compiles once into a small in-memory assembly (`IntrinsicMethodsReference`).

Compilation errors come back as the diagnostic messages joined with `; `. Results are cached in an
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
| `CorValue` — a debuggee `ICorDebugValue` | Everything read from the debuggee; reference values are pinned with strong handles (`EvaluationHandleScope.Root`) so they survive later func evals. |
| `Location` — an `ICilLocation` | Addresses: a debuggee slot (`CorDebugLocation`: local, argument, field, element), a host temporary (`TemporaryLocation`), a synthetic variable (`SyntheticVariableLocation`, a one-element array allocated in the debuggee) or a slot the runtime cannot read at this instruction (`UnavailableLocation`, optimized away - reading it fails with vsdbg's message). A by-reference slot (a `ref` parameter or local, the `this` of a struct method) reads as the location it points to, which the IL then dereferences with `ldind`/`ldobj`. |

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
| `ldc.*`, `ldnull`, `ldstr`, `ldtoken` | Host constants (`ldtoken` pushes the resolved type). |
| `ldarg*`, `ldloc*`, `starg`, `stloc`, `ldarga`, `ldloca` | Read/write through the locations; `ld*` of references roots them. |
| `add … shr.un`, `neg`, `not`, `ceq … clt.un`, `conv.*` | On the host, with int32/int64/float promotion, overflow-checked and unsigned variants, NaN-aware comparisons; enum and small struct values read as integers (an enum through its `value__` field, so the underlying type's sign holds). Host values are written back with wrapping bit reinterpretation (`CilValueEncoding`): an unsigned slot holds its signed twin on the stack, a comparison result stores into a `bool`, and a native integer takes the debuggee's pointer size. |
| `br*`, `beq … ble.un`, `switch` | Branches by instruction index. |
| `ldind/stind/ldobj/stobj/cpobj/initobj` | Through locations; `initobj` creates a default value (zero, null, or a debuggee struct instance). |
| `newarr`, `ldlen`, `ldelem*`, `stelem*`, `ldelema` | `NewParameterizedArray` for primitive and reference element types; `Array.CreateInstance(Type, int)` in the debuggee for other structs (`DateTime[]`), as `ICorDebugEval` cannot allocate those. |
| `isinst`, `castclass` | `Type.GetType(assemblyQualifiedName)` + `Type.IsInstanceOfType(value)` evaluated in the debuggee. |
| `box`, `unbox`, `unbox.any` | Boxes are allocated with `NewParameterizedObjectNoConstructor` and filled byte-wise; unboxing checks the exact class (the object of a boxed primitive is a VALUETYPE of `System.Int32` and friends). `unbox.any Nullable<T>` builds the nullable: an empty one for null, one holding the boxed `T` otherwise. |
| `ldfld/ldflda/stfld`, `ldsfld/ldsflda/stsfld` | `GetFieldValue`, `GetStaticFieldValueAsync` (runs the static constructor on demand). |
| `newobj` | `NewParameterizedObject` with the constructor's declaring-type arguments. |
| `call`, `callvirt` (+ `constrained.`) | See below. |
| `ret` | The result; `nop`/`break` are skipped. |

Anything else (`throw`, exception blocks, delegates, `calli`, pointer arithmetic, `localloc`, …) is a
`NotSupportedException` naming the opcode and IL offset; other failures are wrapped with the offset
and opcode as well.

**Syntax the evaluator does not support** (`Handlers/EvaluationSyntaxTests.UnsupportedSyntaxIsReportedTest`
keeps the list, each form is reported as an error):

- lambdas (`numbers.Any(n => n > 2)`, `((Func<int, int>)(x => x + 1))(2)`): a delegate over code
  that only exists in the expression assembly (the compiler's `<>c` closure class); a delegate the
  debuggee already holds is invoked fine;
- anonymous types (`new { Name = "x" }`): a type of the expression assembly;
- array initializers of constants (`new[] { 1, 2, 3 }`): `RuntimeHelpers.InitializeArray` over a data
  field of the expression assembly (`ldtoken` of a `<PrivateImplementationDetails>` field);
- multidimensional array creation (`new int[2, 3]`): a `newobj` on the array type's own constructor;
- variables declared by patterns (`boxed is int n ? n + 1 : 0`, `count is var any`): Roslyn's
  expression compiler turns declared locals into pseudo-variables (`CreateVariable`/`GetVariableAddress`)
  by rewriting `BoundLocal` references, which a pattern's declaration is not — its code generator then
  fails on the undeclared local. `out var` works;
- string constructors (`new string('x', 3)`): the runtime refuses them in a func eval.

Assignments to a slot of a reference type take the source reference whatever the slot holds
(`boxed = "text"` on an `object` local holding a boxed `int`), values copy their bytes into the
unwrapped destination (an enum or struct the evaluation produced is a box). A constant called through
`constrained.` (`Options.C.ToString()`) is boxed as the constrained type, not as its underlying
integer. Constants fold unchecked (`(sbyte)200` is `-56`), the way the compiler options of the
expression compiler have it.

**Calls.** A call target is one of:

- a *debugger intrinsic* — `CreateVariable`/`GetVariableAddress`/`GetObjectByAlias` implement the
  synthetic variables a declaration expression creates (stored in debuggee-allocated arrays),
  `GetException` the `$exception` alias;
- a method of the expression assembly itself (a lambda or local function), interpreted recursively
  with temporary locals;
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
