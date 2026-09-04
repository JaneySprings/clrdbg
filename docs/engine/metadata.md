# Modules, metadata and symbols

`Metadata/ModuleMetadataReader` is the engine's view of a module's PE metadata and portable PDB —
everything that is not live debuggee state comes from here: source positions, local names, async
stepping information, Source Link, checksums, the entry point. `Models/ModuleInfo` pairs a reader
with the `ICorDebugModule` it describes.

## Loading a module

`ModuleHandler.HandleModuleLoaded` creates the reader from the module's file
(`ModuleMetadataReader.TryLoad(path)`), from the debuggee's memory for in-memory modules
(`ICorDebugProcess.ReadMemory` of the module's image range), or — for a dynamic module
(Reflection.Emit, how mocking libraries build their proxies) — from a copy of the metadata the
runtime's own importer hands out (`IMetaDataTables2.GetMetaDataStorage()`). A dynamic module has no
image and gains metadata with every type defined in it, so its reader is rebuilt on every `LoadClass`
callback (`RefreshDynamicModule`, after `ResetMetaDataInterfaces` so the importer sees the new
metadata), and a module that had nothing to read when it loaded is registered at its first class load.
`System.Reflection.Metadata`'s `PEReader` prefetches the whole image, so the file is not kept open.

Symbols are looked for in the PE's debug directory:

1. A **CodeView** entry with the portable PDB magic (`MinorVersion == 0x504d`) names the PDB; the
   engine expects it **next to the assembly** (the path inside the PE is where it was built) and
   accepts it only when its `BlobContentId` (GUID + stamp) matches the entry and the age is 1 —
   a stale PDB would map to the wrong IL. `ModuleInfo.SymbolFilePath` reports the file.
2. Otherwise an **embedded portable PDB** entry, decompressed in memory.

Without either, `HasSymbols` is false: the module's frames get no source locations, breakpoints
cannot bind to it, and the stepper does not stop in it with `JustMyCode`.

### `ModuleInfo`

| Member | Source |
|---|---|
| `Path`, `Name` | `ICorDebugModule.GetName`. |
| `IsUserCode` | The JIT flags: `CORDEBUG_JIT_DISABLE_OPTIMIZATION` or `CORDEBUG_JIT_ENABLE_ENC` mean the assembly was built for debugging by the user — the Just My Code heuristic. User modules with symbols get `SetJMCStatus(true)` when `JustMyCode` is on. |
| `Version` | The file version (`FileVersionInfo`), falling back to the assembly version from metadata; the adapter formats it as vsdbg does (`1.00.0.0`). |
| `HasSymbols`, `SymbolFilePath` | From the reader. |
| `IsDynamic` | `ICorDebugModule.IsDynamic`: a module the debuggee emitted at run time — no file, no image, no base address. |
| `Module`, `MetadataReader`, `Id` (internal) | The `ICorDebugModule` and the reader behind the info. Modules are looked up (`GetModule`/`FindModule`) by the `ICorDebugModule` object itself — the COM wrappers are one per runtime object for as long as they are held, and a base address could not serve as the key, a dynamic module has none. `Id` is a session-unique number for caches keyed by module. |

Loading a module increments `ManagedDebugger.ModulesVersion`; the expression compiler's caches are
keyed by it, as a new module changes what an expression may bind to.

## What the reader answers

| Method | Reads | Used by |
|---|---|---|
| `GetSourceLocation(methodToken, ilOffset)` | The non-hidden sequence point at the offset, or the closest one before it (the IP at a return is past the last point). Returns a `SourceLocation` with the document path, span, checksum and Source Link URL. | Stack frames, stop locations, step completion. |
| `ResolveBreakpoint(filePath, line, column, requireExactSource, out sourceMismatch)` | The document (exact path, then file name — see below) and `SequencePointResolver`'s choice among its methods' sequence points — see [breakpoints.md](breakpoints.md). | Breakpoint binding, `SetNextStatement`. |
| `ResolveMethodEntry(methodToken)` | The method's first non-hidden sequence point. | Function breakpoints, `stopAtEntry`. |
| `GetEntryPointToken()` | `CorHeader.EntryPointTokenOrRelativeVirtualAddress` when it is a MethodDef and not a native entry point. | `stopAtEntry`. |
| `GetLocalVariableNames(methodToken, ilOffset)` | The names of the locals in the scopes containing the offset, by slot; `DebuggerHidden` and unnamed locals are absent. | Locals, assignments. |
| `TryGetStepRange(methodToken, ilOffset)` | The offsets of the sequence point at/before the IP and of the next one. | Statement-wide step ranges. |
| `GetNextSequencePointOffset(methodToken, ilOffset)` | The first sequence point with source at or after the offset. | Step completion (prolog detection, end of method). |
| `GetAsyncMethodInfo(methodToken)` | Roslyn's async stepping custom debug information (`54FD2AC5-E925-401A-9C2A-F94F171072F8`: catch handler offset, then yield/resume/MoveNext-token triples) and the last sequence point with source. | The async stepper. |
| `GetSourceLink(documentPath)` | The module-level Source Link custom debug information (`CC110556-A091-4D38-9FEC-25AB9A351A6A`), parsed once into a `SourceLinkMap`. | `SourceLocation.SourceLink`. |
| `GetAssemblyVersion()` | The assembly definition. | `ModuleInfo.Version`. |
| `PeMetadataReader`, `PdbMetadataReader`, `Mvid` | The raw readers. | The expression compiler and resolver, function breakpoints, frame signatures. |

Document checksums are reported with their algorithm — SHA-1 (`ff1816ec-…`) or SHA-256
(`8829d00f-…`), other algorithms are dropped — so a client can detect edited sources.

Paths are compared with `\` normalized to `/` and case-insensitively; a PDB built on another machine
(or with `PathMap`) still finds its documents by file name. A file-name match whose checksum equals
the local file's counts as exact; one whose content differs is accepted only with
`ManagedDebugger.RequireExactSource` off (it defaults to on), because any module with an equally named
source would capture the breakpoint otherwise — with it on, such a rejected match is reported through
`sourceMismatch`, which becomes `BreakpointStatus.SourceMismatch`.

## Source Link

`SourceLinkMap` parses the JSON the Source Link targets write into the PDB:

```json
{ "documents": { "C:\\src\\repo\\*": "https://raw.githubusercontent.com/org/repo/<sha>/*",
                 "/_/*":            "https://raw.githubusercontent.com/org/other/<sha>/*" } }
```

A key with a trailing `*` matches documents by prefix and the rest of the path replaces the `*` in
the URL; a key without one must match exactly. When several keys match, the longest (most specific)
wins. Comparison is case-insensitive with normalized separators. The engine only attaches the URL to
locations; downloading is the adapter's business ([debugging.md §9](debugging.md#9-source-link)).

## Signature providers

`System.Reflection.Metadata` decodes signatures through an `ISignatureTypeProvider`; the engine has
one per naming need:

| Provider | Output | Used for |
|---|---|---|
| `Metadata/TypeNameSignatureProvider` | Metadata names: `System.Int32`, ``System.Collections.Generic.List`1<System.String>``, nested types as `Outer.Inner` | Matching function breakpoint patterns against method signatures. |
| `Metadata/DisplayNameSignatureProvider` | C# display: `int`, `List<string>`, `ref int`, `delegate*` | The parameter list in frame names (`Program.Main(string[] args)`). |
| `Evaluation/LocalCountSignatureProvider` | Nothing but the count | Sizing the interpreter's local slots. |
| `EvaluationMetadataResolver.SignatureNameProvider` | Metadata names with `+` for nesting | Comparing a referenced signature with the definitions of a type. |
| `EvaluationMetadataResolver.RuntimeTypeSignatureProvider` | `ResolvedCilType` | Turning signature types into runtime types for the interpreter. |

## Reading metadata at runtime

Besides the PDB, the engine reads the *live* metadata of the debuggee's modules through
`IMetaDataImport` (`ICorDebugModule.GetMetaDataInterface<IMetaDataImport>()`): type and member
names, field/method attributes, custom attributes (`DebuggerDisplay`, `DebuggerTypeProxy`,
`DebuggerBrowsable`, `Flags`, the step-filter attributes in `Metadata/AttributeNames`), enum literals
and nested type lookups. `Extensions/MetadataImportExtensions` adds the small helpers the engine needs
(`IsStatic`/`IsLiteral` on field tokens, `IsNonUserMethod`/`IsPropertyOrOperator` on method tokens,
`HasAttribute`, `FindTypeDef`/`FindNestedTypeDef`, `FindProperty`), and `Variables/CustomAttributeReader`
decodes attribute blobs (the prolog, a string
constructor argument, string/`Type` named arguments, the `DebuggerBrowsableState` integer).
