# Variables

`Variables/VariableProvider` produces the `VariableInfo` lists a client shows — a frame's locals and
the children of any value — using `ValueFormatter`/`TypeNameFormatter` for the text,
`VariableWriter` for assignments, and `VariableManager`/`FrameReferenceManager` for the handles that
tie a client's later requests back to debuggee objects.

## References and handles

A `VariableInfo` with children carries a non-zero `VariablesReference`. Behind it `VariableManager`
stores a `VariableReference`: the thread and frame depth, the `ICorDebugValue`, the
`DebuggerTypeProxy` instance when there is one, the evaluate name of the value, and a kind:

| Kind | Lists |
|---|---|
| `Scope` | The locals of a frame (`GetLocalsReference` issues it; zero when the frame has neither locals, arguments nor a current exception). |
| `Members` | The members of an object or the elements of an array. |
| `StaticMembers` | The `Static members` group of a value. |
| `NonPublicMembers` | The `Non-Public members` group of a value. |
| `NonPublicStaticMembers` | The `Non-Public members` group inside `Static members`. |
| `RawMembers` | The value's own members under the `Raw View` group of a `DebuggerTypeProxy` expansion. |
| `ResultsView` | The `Results View` node of an `IEnumerable` value, enumerated in the debuggee when expanded. |

Frames are likewise handles: `FrameReferenceManager` maps an id to (thread, depth), and every use
re-walks the thread's chains (`ManagedDebugger.GetFrame`), because `ICorDebugFrame` objects are
neutered whenever the debuggee runs — including during a func eval in the middle of listing members.

Values obtained through a func eval (property getters, `DebuggerDisplay` results, proxies) are
`ICorDebugHandleValue`s that keep their object alive. One that ends up behind a reference is kept;
one that is displayed as a leaf is released at once. `Continue()`/`StepAsync` clear both managers,
releasing every kept handle (`TryDispose`, deduplicated: a value and its `Static members` group
share one).

## A frame's scope

`GetVariablesAsync(scope)` lists, in this order:

1. **`$exception`** — the thread's current exception, if any.
2. **`this` and the arguments.** `ICorDebugILFrame.GetArguments` includes the implicit `this` of
   instance methods, the metadata parameters do not, so the first argument is split off for
   instance methods. When the method is `MoveNext` or a lambda (`>b` in the name) on a Roslyn
   generated type (`GeneratedNameParser`: state machine or display class), that `this` is the
   generated object: the user's `this` is read from its `<>4__this` proxy field (following `<>8__`
   links to enclosing closures), and the generated object becomes the source of the hoisted locals.
3. **Hoisted locals** — the fields of the closure/state machine and of every enclosing closure,
   shown under their original names (`<count>5__1` → `count`); other generated fields are hidden.
4. **IL locals**, named through the PDB local scopes that contain the current IL offset
   (`ModuleMetadataReader.GetLocalVariableNames`); slots without a name (compiler temporaries,
   `DebuggerHidden`) are skipped.

Arguments and locals get `EvaluateName` equal to their name, so a client can re-evaluate or watch
them.

## Expanding a value

Members are listed for the exact runtime type, then its base types up to `System.Object`,
`System.ValueType` or `System.Enum`. Base types are walked with the engine's `GetBaseType()`, which
reports no base where `ICorDebugType.GetBase` returns a null type locally but an element-type-less
placeholder over the remote (mobile) transport:

- **Fields** are read with `ICorDebugObjectValue.GetFieldValue`; static fields through
  `FuncEvalRunner.GetStaticFieldValueAsync`, which runs the type's static constructor
  (`NewParameterizedObjectNoConstructor`) when the runtime reports `CORDBG_E_STATIC_VAR_NOT_AVAILABLE`
  / `CORDBG_E_CLASS_NOT_LOADED`; literal (`const`) fields are formatted straight from metadata.
- **Properties** are read by evaluating their getter with the *reference* value as receiver (a
  dereferenced object cannot be passed to a func eval) and the exact type's type arguments; properties
  without a getter are skipped. A getter that throws is a failed read — an error entry reading
  `'Name' threw an exception of type 'System.InvalidOperationException'`, the wording of Microsoft's
  debugger — never the thrown exception presented as the property's value.
- **Visibility.** `Kind` (`Data`/`Property`, plus `Group` for the group nodes below and `ResultsView`)
  and `Visibility` (`Public`/`Private`/`Protected`/`Internal`) come from the field/getter attributes.
  Every type shows its public members inline and groups the non-public ones under `Non-Public members`
  when they exist. Static members go into a `Static members` group; group nodes have `Kind = Group`.
- **`DebuggerBrowsable`**: `Never` hides the member, `RootHidden` replaces an array-valued member by
  its elements.
- **`DebuggerTypeProxy`**: the proxy is instantiated in the debuggee (`.ctor(value)`, the
  first constructor found by name) and its public members are listed instead of the value's, which
  remain reachable through a `Raw View` group.
- **Arrays** list `[i]` elements, `[i, j]` for a multidimensional array (the logical indices, honouring
  non-zero lower bounds). The elements are one *block* slot in the listing (`VariableSlot.Count`), named
  and read by offset, so listing an array costs nothing per element — a ten-million-element array
  builds one slot, and only the page requested is ever named, read and formatted. Elements are re-read
  from the source value when their page is requested, as a dereferenced array value is neutered by any
  func eval; an empty array has no children.
- **`Results View`**: a value whose type implements `IEnumerable`/`IEnumerable<T>` (checked on the
  type and its bases) gets a `Results View` node (`Kind = ResultsView`, the adapter marks it as having
  side effects) whose expansion enumerates the value in the debuggee — `System.Linq.Enumerable.ToArray`
  over it, loading `System.Linq` first when the debuggee has not — and lists the array's elements with
  `new System.Linq.SystemCore_EnumerableDebugView<T>(value).Items[i]` evaluate names; an empty
  enumeration shows the `Empty` row ("Enumeration yielded no results") the way VS does. A
  `DebuggerTypeProxy` expansion has no `Results View`, its `Raw View` does not either.
- **Members are read once per type.** Listing a type's members reads the metadata of each field and
  property getter a single time; the static, literal and visibility decisions and the func-eval of a
  getter are taken from that copy when the page holding the member is materialized.
- Members are sorted ordinally, groups last and in this order: `Static members`, `Non-Public members`,
  `Raw View`, `Results View`; a member that cannot be read becomes an error entry (`IsError`, the
  message as the value).

**Evaluate names** are full expressions: `parent.Member` for instance members, `Namespace.Type.Member`
for statics, `parent[0]` for elements, the bare name for hoisted locals.

A value gets a children reference (`CreateChildrenReference`) when it is a non-empty array or an
object of element type `CLASS`/`VALUETYPE`/`SZARRAY`/`ARRAY` — after unwrapping `Nullable<T>` to its
value — and never for strings, decimals and boxed primitives, which are displayed as leaves.

## Formatting values

`ValueFormatter.Format(value, escapeStrings)` returns the type name and display text, and flags the
cases that need code to run in the debuggee:

| Value | Text |
|---|---|
| primitives | Invariant-culture numbers, `true`/`false`, chars as `97 'a'`. |
| `string` | C# literal (quoted and escaped) in variable views, raw for evaluation results used internally. |
| `null` reference | `null`, with the static type's name. |
| array | `{int[60]}`, `{string[2, 3]}`. |
| enum | The member name, `A \| B` for `[Flags]` values fully decomposed into members, the number otherwise. |
| `Nullable<T>` | `null` or the value's text, typed `int?`. |
| `decimal` | Read from the struct's 16 bytes (`flags, hi, lo, mid`) through `new decimal(bits)`. |
| object with `DebuggerDisplay` | The attribute string as an interpolated-string *template* (`Count = {Count}`; anonymous types' `\{ … }` fixed up; a `Name` argument becomes a `Name = ` prefix), evaluated against the object ([evaluation.md](evaluation.md)). `{Name,nq}` format specifiers are stripped by the compiler. |
| exception, or a type overriding `ToString()` | The `{ToString()}` template, evaluated the same way. |
| any other object | `{Namespace.Type}`. |

A failed template evaluation makes the variable an error entry with the error text — except a
time-out: an implicit evaluation the engine had to abort ([evaluation.md](evaluation.md)) falls back to
`{TypeName}` like one past the listing's two-second implicit-eval budget, the way Microsoft's debugger
shows a value whose evaluation it cut off.
`TypeNameFormatter` renders types as C#: keywords for primitives, `string[]`/`int[,]`, generic
instantiations with the arguments consumed by arity along the nesting chain (`Outer<string>.Inner<int>`),
`System.Nullable<T>` as `T?`, `System.String`/`System.Object`/`System.Decimal` and boxed primitives as
their aliases.

## Assignments

`SetVariableAsync(reference, name, text)` finds the variable by name in the scope (locals by PDB
name, then parameters by metadata name) or among the members (`[i]` elements, fields on the type and
its bases), and `VariableWriter` writes it: `null` into reference slots, and parsed primitives
(`bool`, `char` — `'a'`, `a` or a code —, integers, `float`/`double`, `nint`/`nuint`) into generic
values, after checking the size matches. Anything else ("Only primitive values are supported") is
reported as an error; the updated value is returned formatted like any variable.
