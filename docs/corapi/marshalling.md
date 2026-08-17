# Marshalling in CorApi

Everything that crosses the managed/native boundary in this library goes through
**compile-time source-generated interop** — there is no runtime-generated COM plumbing
and no `[ComImport]` anywhere. This page explains the machinery: how the COM interfaces
become callable objects, how the callback object is exposed *to* the runtime, how
`DbgShim`'s P/Invokes are generated, and where custom marshallers step in.

## Why source-generated

The classic .NET COM interop path (`[ComImport]`, runtime-built RCWs) depends on the
built-in COM support that only exists on Windows. The CLR debugging interfaces,
however, are plain IUnknown-style vtable interfaces that `mscordbi` implements on every
OS. Source-generated COM (`[GeneratedComInterface]`, .NET 8+) targets exactly that
shape:

- all marshalling code is emitted **at build time** by `Microsoft.Interop.ComInterfaceGenerator`
  and is ordinary, inspectable C#;
- it works cross-platform and is NativeAOT/trimming-compatible (no runtime code gen);
- it is built on `ComWrappers`, so the library controls object identity and lifetime
  instead of the legacy RCW machinery.

## `DisableRuntimeMarshalling`

The assembly is marked `[assembly: DisableRuntimeMarshalling]`. That switches off the
runtime's implicit marshalling layer entirely:

- structs passed to native code must be **blittable** — what you declare is the exact
  memory layout that crosses the boundary;
- nothing is converted silently: `bool` parameters are annotated explicitly
  (`[MarshalAs(UnmanagedType.Bool)]` → 4-byte Win32 `BOOL`), strings and arrays are
  handled by the generators, never by the runtime;
- combined with the generators, every byte that crosses the boundary is accounted for
  in generated or hand-written marshaller code.

This is why the native struct mirrors in `Structs/` look the way they do — field-exact
layouts of the SDK structures (`CorDebugStepRange { uint startOffset; uint endOffset; }`,
`CordbAddress`, `CorHeapObject`, …) that can be passed straight through.

## Consuming COM interfaces (the RCW side)

Each of the 148 interfaces is declared as:

```csharp
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("3D6F5F63-7538-11D3-8D5B-00104B35E7EF")]
public partial interface ICorDebugAppDomain : ICorDebugController { ... }
```

At build time the generator emits, per interface: the unmanaged vtable layout, a
managed proxy implementation that forwards each method through the vtable pointer, and
the glue that registers both with a `StrategyBasedComWrappers` instance. When a native
method returns an interface pointer (`out ICorDebugProcess ppProcess`), the generated
code wraps it in a managed proxy object; when a managed proxy is passed back in, the
original COM pointer is unwrapped.

Three properties of that object model matter in daily use:

- **Casting is `QueryInterface`.** The proxy objects implement the runtime's dynamic
  interface-cast hook, so a plain C# cast between `[GeneratedComInterface]` types is
  answered by a native QI on the underlying pointer. `(process as ICorDebugProcess5)`
  returns a usable interface or `null` exactly as the native object supports it — this
  is how the versioned-interface families (`ICorDebugThread2`, `ICorDebugILFrame4`,
  `IMetaDataImport2`, …) are reached, and it is what the `GetProcess5()`-style widening
  extensions wrap.
- **Identity is COM identity.** By default (`ComInterfaceMarshaller<T>`), pointers with
  the same IUnknown identity map to the same managed wrapper, so reference equality on
  wrappers matches native identity.
- **`UniqueComInterfaceMarshaller<object>`** is used where the API returns an interface
  the caller will re-cast to an arbitrary type, and a *fresh, non-cached* wrapper is
  required — `ICorDebugModule.TryGetMetaDataInterface(ref Guid riid, out object ppObj)`
  and `ICorDebugModule3.TryCreateReaderForInMemorySymbols` are the notable sites. The
  returned `object` is then cast to `IMetaDataImport`, `IMetaDataTables`, etc.

Every method is `[PreserveSig] int TryXxx(...)`: the generator is told *not* to convert
HRESULTs into exceptions, so failure handling stays explicit at the call site (and
`S_FALSE`-style success codes remain distinguishable).

## Exposing the callback (the CCW side)

The single object that native code calls *into* is:

```csharp
[GeneratedComClass]
public partial class CorDebugManagedCallback :
    ICorDebugManagedCallback, ICorDebugManagedCallback3,
    ICorDebugManagedCallback4, ICorDebugManagedCallback2 { ... }
```

`[GeneratedComClass]` makes the generator emit the COM-callable wrapper: vtables for
all four callback interfaces whose slots invoke the class's explicit interface
implementations. Passing the instance to `ICorDebug.SetManagedHandler(...)` marshals it
out as a COM object; from then on `mscordbi` delivers every debug event as a native
vtable call that arrives on the managed methods, which package the arguments into
`EventArgs` and raise the corresponding .NET event. Interface arguments of the
callbacks (`ICorDebugAppDomain pAppDomain`, `ICorDebugThread pThread`, …) are wrapped
into proxies by the same generated machinery, in the native→managed direction.

## Strings and arrays

The interfaces declare `StringMarshalling.Utf16`; native strings are UTF-16 characters
with explicit-length buffers, following the Win32 two-call convention:

```csharp
[PreserveSig] int TryGetName(uint cchName, out uint pcchName,
    [MarshalUsing(CountElementName = "cchName")] char[]? szName);
```

Call once with `(0, out length, null)` to size, allocate `char[length]`, call again,
trim the trailing NUL. The extension helpers (`GetName()`) implement exactly this loop,
retrying if the reported size grows between the calls.

Arrays use the same explicit-length pattern via `[MarshalUsing(CountElementName = ...)]`
tying the buffer to its count parameter (`celt`, `cbSignature`, `contextSize`,
`nTypeArgs`, …). Raw signature blobs deliberately stay unmarshalled — returned as
`nint` + length and parsed by the consumer per ECMA-335.

In `DbgShim`, the classic `[MarshalAs(UnmanagedType.LPArray, SizeParamIndex = n)]`
form appears for the same purpose (`CreateVersionStringFromModule`'s buffer).

## Custom marshallers

Six `[CustomMarshaller]` types cover the cases the generators can't express directly:

| Marshaller | For | Why |
|---|---|---|
| `CorActiveFunctionMarshaller` | `CorActiveFunction` | struct embeds `ICorDebugAppDomain`/`Module`/`Function2` pointers |
| `CorDebugBlockingObjectMarshaller` | `CorDebugBlockingObject` | struct embeds an `ICorDebugValue` pointer |
| `CorDebugExceptionObjectStackFrameMarshaller` | `CorDebugExceptionObjectStackFrame` | struct embeds an `ICorDebugModule` pointer |
| `CorDebugGuidToTypeMappingMarshaller` | `CorDebugGuidToTypeMapping` | struct embeds an `ICorDebugType` pointer |
| `CorGcReferenceMarshaller` | `CorGcReference` | struct embeds domain/value pointers |
| `EnumeratorMax1Marshaller` | `uint cMax` on `IMetaDataImport.Enum*` | contract guard, see below |

The struct marshallers all follow one pattern: a blittable `Native` mirror struct in
which each interface field is an `nint`, `ConvertToUnmanaged`/`ConvertToManaged`
implementations that translate pointer↔proxy with `ComInterfaceMarshaller<T>`, and a
`Free` that releases the native reference — so a struct that *contains* COM objects can
cross the boundary inside arrays (`EnumerateGCReferences`, `GetActiveFunctions`,
`GetBlockingObjects`, …) with correct reference counting.

`EnumeratorMax1Marshaller` is different: it marshals nothing. The native metadata
`Enum*` functions fill caller-provided arrays (`uint* rTypeDefs, uint cMax`), but the
interfaces here declare the result as a single `out TypeDefToken` for type-safety. The
marshaller enforces the resulting contract — it throws if a caller ever passes
`cMax != 1` — turning a potential buffer-overrun mistake into an immediate exception.
The `IEnumerable<TypeDefToken>`-style extension adapters drive these one-token-at-a-time
cursors.

## `DbgShim`: generated P/Invokes

`DbgShim` is a `static unsafe partial class` of `[LibraryImport("dbgshim")]` partial
methods with `[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]`. The
LibraryImport generator emits each method body — UTF-16 string pinning, `out`
parameter plumbing, the `bool`→`BOOL` conversions — around an inner `DllImport` stub,
at build time (the same "no runtime marshalling" rule applies).

Two details worth knowing:

- The startup-notification functions take a raw unmanaged function pointer
  (`delegate* unmanaged[Cdecl]<void*, void*, int, void> pfnCallback`). The managed
  target must be a static method marked
  `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]`, and dbgshim invokes it
  from a native thread — no managed context should be assumed inside it beyond handing
  the `ICorDebug*` off (wrapped via `ComInterfaceMarshaller<ICorDebug>`) and signaling.
- The `dbgshim` native library itself is resolved by the standard `NativeLibrary`
  search (in this repository it is deployed next to the application by the
  `Microsoft.Diagnostics.DbgShim` package). `CreateDebuggingInterfaceFromVersion*`
  then loads the runtime-matched `mscordbi`, which is where all the COM interfaces
  above are actually implemented.
