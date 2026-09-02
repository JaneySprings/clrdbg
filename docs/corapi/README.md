# DotNet.Debugging.CorApi

`DotNet.Debugging.CorApi` is the managed interop layer between this repository's
debugger engine and the CLR's **native debugging services**. It contains no debugger
logic of its own: it is a faithful, thin projection of three unmanaged API families
into C#, plus the P/Invoke surface needed to bootstrap a debug session.

| API family | Interfaces | Purpose |
|---|---|---|
| `ICorDebug*` | 136 | The CLR debugging API: processes, app domains, assemblies, modules, threads, chains, frames, code, breakpoints, steppers, values, evals, GC heap inspection, managed callbacks. The same API used by vsdbg and Visual Studio. |
| `IMetaData*` | 6 | Raw metadata reading/inspection for debuggee modules: `IMetaDataImport`/`2`, `IMetaDataAssemblyImport`, `IMetaDataTables`/`2`, `IMetaDataInfo`. |
| `ICLR*` | 6 | Runtime discovery and out-of-process inspection entry points: `ICLRDebugging`, `ICLRMetaHost`, `ICLRRuntimeInfo`, `ICLRDebuggingLibraryProvider`/`2`/`3`. |
| `DbgShim` | 16 functions | P/Invoke wrappers for the native `dbgshim` library — process launch, runtime-startup notification, and `ICorDebug` acquisition. |

## The API model

**Raw interfaces, raw HRESULTs.** Every COM method is exposed exactly as the native
signature dictates, as a `[PreserveSig] int TryXxx(...)` method — the `int` is the
HRESULT, never converted to an exception automatically:

```csharp
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugappdomain-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("3D6F5F63-7538-11D3-8D5B-00104B35E7EF")]
public partial interface ICorDebugAppDomain : ICorDebugController {
    [PreserveSig] int TryGetProcess(out ICorDebugProcess ppProcess);
    [PreserveSig] int TryGetName(uint cchName, out uint pcchName, char[]? szName);
    // ...
}
```

Each type carries a single `// <url>` comment pointing at its official Microsoft Learn
reference page — that page is the documentation for the type and its members.

**Throwing convenience layer.** The `DotNet.Debugging.CorApi.Extensions` namespace adds
classic extension methods over the interfaces the engine uses. They wrap the `Try*`
calls with `Marshal.ThrowExceptionForHR`, hide the two-call buffer-sizing dance of
native string getters (`NativeStrings`), and fetch the items of a COM enumerator in one
round trip (`CorDebugEnumExtensions.ToArray`: the count, then a single `Next`):

```csharp
var name = module.GetName();                       // TryGetName + throw on failure
foreach (var chain in thread.GetChains()) { ... }  // TryEnumerateChains + GetCount + Next
```

Call sites choose per call: `TryXxx` when an HRESULT is an expected outcome (a common
situation in debugging — see [debugging.md](debugging.md)), the extension form when
failure is exceptional.

**Interface versions via casts.** The native API grows by versioned interfaces
(`ICorDebugProcess` … `ICorDebugProcess11`, `ICorDebugThread`/`2`/`3`/`4`, …) obtained
through COM `QueryInterface`. In this library a plain C# cast performs the QI (see
[marshalling.md](marshalling.md)), and widening helpers make it explicit:

```csharp
public static ICorDebugProcess5 GetProcess5(this ICorDebugProcess instance) =>
    (instance as ICorDebugProcess5)
        ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess5.");
```

**Typed native vocabulary.** Metadata tokens are `readonly record struct`s
(`TypeDefToken`, `MethodDefToken`, `MetadataToken`, … with `Rid`, `Type`, `Value`,
`IsNil` — the native `mdTypeDef`/`mdMethodDef`/`mdToken` typedefs under C# names),
flags and options are real enums (`CorElementType`, `CorMethodAttr`,
`CorDebugJITCompilerFlags`, …), native structs are mirrored one-to-one
(`CorDebugStepRange`, `CorHeapObject`, `CordbAddress`, …), and `Cor` is a constants
catalog of ~400 HRESULT codes and well-known names (`S_OK`, `CORDBG_E_PROCESS_TERMINATED`,
`CORDBG_E_CLASS_NOT_LOADED`, …) for interpreting `Try*` results.

## Documents

| Document | Contents |
|---|---|
| [debugging.md](debugging.md) | How a debug session flows through this library: dbgshim bootstrap, the callback pump, the stop/go model, breakpoints and stepping, value inspection, metadata, function evaluation |
| [marshalling.md](marshalling.md) | The interop machinery: source-generated COM, disabled runtime marshalling, the callback CCW, LibraryImport, string/array conventions, custom marshallers |
