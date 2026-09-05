# DotNet.Debugging.Evaluation

`DotNet.Debugging.Evaluation` is the Roslyn C# expression compiler — the code the Visual Studio
debugger compiles watch expressions, conditions and `DebuggerDisplay` templates with — rebuilt from
the Roslyn sources without the Visual Studio debugger dependency, plus the small API
[`DotNet.Debugging.Engine`](../engine/README.md) drives it through.

```
DotNet.Debugging.Engine ──ExpressionContext.Compile()──> DotNet.Debugging.Evaluation ──internals──> Microsoft.CodeAnalysis.CSharp
                                                          (Roslyn/src, verbatim + Shims/)
```

## Why a project of our own

- The compiler lives in the Roslyn repository (`src/ExpressionEvaluator/*/Source/ExpressionCompiler`)
  and is not published as a package. It is written against the `Microsoft.VisualStudio.Debugger.Engine`
  ("Dkm") contract, which only exists inside Visual Studio.
- Everything in it is `internal`, and it reaches into the internals of `Microsoft.CodeAnalysis` and
  `Microsoft.CodeAnalysis.CSharp`. The released nuget assemblies grant `InternalsVisibleTo` to two
  assembly names — `Microsoft.CodeAnalysis.ExpressionEvaluator.ExpressionCompiler` and
  `Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.ExpressionCompiler` — signed with Microsoft's
  shared public key. Any rebuild has to carry one of those names and that key.
- Owning the sources lets the engine call the compiler directly (`ExpressionContext`) instead of
  resolving private members by name at runtime, and lets it use the C# compiler's own
  `GeneratedNameParser` for closure and state machine fields (`GeneratedNames`).

## Layout

| Path | Contents |
|---|---|
| `Roslyn/src/` | A verbatim copy of `src/ExpressionEvaluator/Core/Source/ExpressionCompiler`, `src/ExpressionEvaluator/CSharp/Source/ExpressionCompiler` and the one file they link from elsewhere, `src/Test/PdbUtilities/Shared/DateTimeUtilities.cs`, taken from `dotnet/roslyn` at the commit the referenced `Microsoft.CodeAnalysis.CSharp` package was built from. **Never edited** — every adaptation lives in the project file and `Shims/`, so an update is a plain replacement. |
| `Roslyn/Roslyn.props` | Written by `update.sh`: the vendored version and commit. The build fails when the version differs from `CodeAnalysisVersion` in `Directory.Packages.props`. |
| `Roslyn/update.sh` | Re-fetches the sources for a version (see [Updating Roslyn](#updating-roslyn)). |
| `Roslyn/MicrosoftSharedPublicKey.snk` | The public half of the key Roslyn's `InternalsVisibleTo` names (public key token `31bf3856ad364e35`), extracted from the attribute itself. Public signing needs no private key. |
| `Roslyn/License.txt` | Roslyn's MIT license, copied with the sources. |
| `Roslyn/.editorconfig` | Silences, for the vendored files only, the analyzer categories the repository raises to warnings and the nullable findings of a newer compiler. |
| `Shims/` | What the surviving sources still name from the excluded files: the Dkm enums the compiler reports results with (`DkmEvaluationFlags`, `DkmClrAliasKind`, …) and two data holders (`DkmContracts.cs`, members and values decoded from the `Microsoft.VisualStudio.Debugger.Engine` assembly), `ResultPropertiesHelper` (the `GetResultProperties` method of the excluded `DkmUtilities`, copied verbatim) and an empty `ExpressionCompiler` stand-in (`MetadataUtilities` locates an embedded resource through `typeof(ExpressionCompiler).Assembly`). All internal; they carry Roslyn's namespaces, not the project's — the vendored files are not redirected. |
| root | The API the engine uses: `ExpressionContext` (a method or type context that compiles expressions), `EvaluationMetadata` / `ModuleMetadataBlock` (the loaded modules' metadata), `ExpressionCompileResult`, `ReuseConstraints` (the IL span a method context stays valid for), `GeneratedNames` / `GeneratedNameKind`, and `IntrinsicMethodsReference` (the debugger intrinsics assembly the compiler emits calls into). |

## How it builds

The project file is where all the adaptation happens:

- **Assembly identity.** `AssemblyName` is `Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.ExpressionCompiler`
  and the assembly is public-signed with `Roslyn/MicrosoftSharedPublicKey.snk`. That is the identity
  Roslyn's `InternalsVisibleTo` grants internals to; the runtime checks the name and public key when an
  internal member is accessed and .NET does not verify strong name signatures, so the public half
  suffices. The output file on disk is therefore named after Roslyn's assembly, not after the project.
- **One assembly instead of Roslyn's two.** Upstream builds a language neutral core and a C# assembly;
  the C# name is granted internals to everything the core needs, so both source trees compile into
  the single assembly here and the core's `InternalsVisibleTo` to the C# assembly becomes moot.
- **Excluded files.** `<Compile Remove>` drops the eleven files written against the Dkm classes: the
  compiler's Dkm entry points (`ExpressionCompiler`, `CSharpExpressionCompiler`, `CSharpMetadataContext`),
  `DkmUtilities`, `ExpressionEvaluatorFatalError` (reports Dkm exceptions), and the frame and
  instruction decoders (Visual Studio's stack frame names — the engine formats its own). Nothing else
  in the sources depends on Dkm beyond what `Shims/` provides. Two things the entry points did are not
  reproduced: retrying a compilation whose only errors are duplicate-type ambiguities with the frame's
  direct references only, and retrying with more metadata when an assembly is missing — the engine
  passes every loaded module up front.
- **Constants.** `EXPRESSIONCOMPILER` is defined as in Roslyn's project. `DEBUG` is stripped in every
  configuration: the nuget Roslyn assemblies are Release builds and the members they expose only under
  `DEBUG` (extra parameters of `LocalSymbol.WithSynthesizedLocalKindAndSyntax`, `MethodSymbolAdapter`, …)
  do not exist, so a build with `DEBUG` defined fails against them.
- **Compiler settings.** `ImplicitUsings` is off (the sources bring their own usings), the
  repository's `InternalsVisibleTo` to the test project is removed (a strong named assembly must name a
  key for its friends) and `WindowsProxy.winmd` is embedded under the logical name `MetadataUtilities`
  looks it up by.
- **The private constructor.** Roslyn creates its `EvaluationContext` from a symbol reader only and keeps
  the constructor private; the engine reads portable PDBs through `System.Reflection.Metadata` instead.
  `ExpressionContext` binds to the constructor with an `UnsafeAccessor` — no runtime reflection, no edit
  to the vendored file — which fails at first use, naming the member, should an update change its
  signature.

## The API

Everything the engine does with the compiler goes through `ExpressionContext`; the Roslyn types never
cross the project boundary:

```csharp
// Once per module set: where each loaded module's raw metadata lives (one module per assembly identity)
var metadata = new EvaluationMetadata(modules.Select(m => new ModuleMetadataBlock(m.Mvid, m.Name, m.GenerationId, m.Pointer, m.Size)).ToList());

// A frame: the method's locals, the hoisted locals in scope at the (normalized) IL offset, the PDB's imports and constants
var ilOffset = ExpressionContext.NormalizeILOffset(rawOffset);
var context = ExpressionContext.CreateMethodContext(metadata, mvid, moduleName, methodToken, localSignatureToken, ilOffset, pdbReader);
// Or a value alone, for a DebuggerDisplay template
var typeContext = ExpressionContext.CreateTypeContext(metadata, mvid, moduleName, typeToken);

var result = context.Compile("items.Count * 2", hasException: false);   // '$exception' is available when true
if (result.Assembly == null)
    throw new EvaluationException(string.Join("; ", result.Errors));
// result.Assembly is an in-memory PE with result.TypeName.result.MethodName taking the frame's arguments and locals

// A method context serves every IL offset of its reuse span; the engine keeps this with the cached expression
var reusable = context.ReuseConstraints!.AreSatisfied(mvid, moduleName, methodToken, otherOffset);

// The C# compiler's own knowledge of the names it generates
GeneratedNames.GetKind("<>c__DisplayClass0_0");                 // GeneratedNameKind.LambdaDisplayClass
GeneratedNames.TryParseGeneratedName("<count>5__1", out var kind, out var open, out var close);   // HoistedLocalField, "count"
```

The compiled method calls into `Microsoft.VisualStudio.Debugger.Clr.IntrinsicMethods` for aliases and
synthetic variables; `IntrinsicMethodsReference` declares that type in a small assembly referenced by
every compilation, and the engine's interpreter recognizes the calls by name. See
[docs/engine/evaluation.md](../engine/evaluation.md) for what the engine does with the result.

## Updating Roslyn

The sources reach into the internals of the referenced assemblies, so they must come from the same
Roslyn version — the build refuses a mismatch. To move to a new `Microsoft.CodeAnalysis.CSharp`:

1. Bump `CodeAnalysisVersion` in `Directory.Packages.props`.
2. Run `DotNet.Debugging.Evaluation/Roslyn/update.sh` (needs `git` and `curl`). It reads the source
   commit from the package's nuspec on nuget.org, fetches just the vendored paths from `dotnet/roslyn`
   at that commit (a sparse, blobless, depth-1 fetch of about a megabyte), replaces `Roslyn/src` and
   `Roslyn/License.txt` and rewrites `Roslyn/Roslyn.props`. Running it again for the same version
   yields no diff, which is also how to check the copy is still pristine.
3. Build. The expression compiler is stable — between 5.6.0 and 5.9.0 upstream touched 6 of its files,
   27 lines — so an update usually builds as is. When it does not:
   - a vendored file newly names a Dkm type: add a stand-in to `Shims/DkmContracts.cs` or, if the file
     is Dkm-facing through and through, add it to the `<Compile Remove>` list;
   - `ResultPropertiesHelper` no longer matches `GetResultProperties` in the excluded
     `Roslyn/src/.../Core/Source/ExpressionCompiler/DkmUtilities.cs`: copy the new version over;
   - the `EvaluationContext` constructor changed: update the `UnsafeAccessor` declaration in
     `ExpressionContext` (the build does not check it, the first evaluation does — the tests do);
   - new nullable warnings inside `Roslyn/src`: add their codes to `Roslyn/.editorconfig`.
4. Run the evaluation tests (`EvaluateTests`, `EvaluationSyntaxTests`, `EvaluationCornerTests`,
   `EvaluationTypeTests`, `ClosureVariableTests`, `VariableTests`, `ExceptionTests`).
5. Commit the vendored sources, `Roslyn.props` and the version bump together.

## Attribution

The sources under `Roslyn/src` are Roslyn's, licensed under MIT by the .NET Foundation
(`Roslyn/License.txt`). See [ATTRIBUTION.md](../../ATTRIBUTION.md).
