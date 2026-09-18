# The host library

`Host/` builds `remotecoreclrhost`, the library the debugger loads: C# published with NativeAOT as a self-contained,
trimmed shared library. Its one export, `CreateRemoteCordbObject(address, port, platform, isServer, assembliesPath,
out ICorDebug)`, is what dbgshim calls for a remote target; the object it returns is `RemoteCorDebug`, an `ICorDebug`
implemented with CorApi's own interface definitions (`GeneratedComClass` over `GeneratedComInterface`), handed out
through the same marshaller the generated interfaces use.

| File | Content |
|---|---|
| `Exports.cs` | the export |
| `RemoteCorDebug.cs` | the `ICorDebug`: reaching the agent, delivering the callbacks, replaying an attach |
| `RemoteDebuggerClient.cs`, `RemoteAgentListener.cs` | the protocol client: frames, sequences, the event channel, batched releases |
| `Protocol/` | the wire types and the `RemoteArgument` kinds |
| `Proxy/RemoteSession.cs` | one proxy per handle, the kinds of values and frames, the replay state |
| `Proxy/RemoteObject.cs` | the proxy base: the invoke helpers, the name cache, blobs |
| `Proxy/Remote*.cs` | one proxy class per kind of object the runtime's debugging library makes |
| `RemoteAgent.cs`, `HostLog.cs` | the launcher's environment names and the protocol version; the Debug log |

## Reaching the agent (`RemoteCorDebug`)

As the debugger's server (Mac Catalyst, the simulator) the object listens from its construction, before the launch
returns, and takes the one agent that connects. As a client it connects to the address and port, retrying every
250 ms for two minutes while the app starts; a forwarded port accepts the connection before the app listens behind it
and drops it right after, so only a connection whose `Hello` is answered within five seconds counts. `Hello` comes
first on either path: it checks the protocol version and says whether the attach callbacks are coming or are to be
replayed. Then the callback events are subscribed to; events that arrived before that waited in the client's channel,
in order.

`Initialize`, `SetManagedHandler` and `CanLaunchOrAttach` succeed; `DebugActiveProcess` answers `E_NOTIMPL`, as the
engine expects of a remote attach, whose `ICorDebugProcess` arrives with the `CreateProcess` callback; `Terminate` and
the process's `Detach` close the connection.

## Delivering the callbacks

Each event is decoded in the callback's argument order, every handle into a proxy (`RemoteCallbackArguments`), and
handed to the engine's `ICorDebugManagedCallback` (`ICorDebugManagedCallback2` for the exception dispatch callbacks).
The engine continues the controller when it is done, which reaches the agent as a `Continue` on the controller's
proxy. A callback the engine is not handed (function remaps, connections, MDA notifications, or anything before the
managed handler is set) is continued by the host on the controller the event names (the process when it names none)
and its handles are given back at once. `ExitProcess`, and a lost connection, are reported to the engine as
`ExitProcess`, once.

## Replaying an attach

When `Hello` brings a process handle the attach callbacks went to an earlier host, or to none: the host rebuilds them.
It stops the process, hands the engine `CreateProcess`, then `CreateAppDomain` for each app domain, `LoadAssembly` and
`LoadModule` for each assembly and its modules, and `CreateThread` for each thread, in the order the runtime reports an
attach. The engine continues after each callback as it always does; during the replay the session holds those
`Continue` calls instead of forwarding them (the process and app domain proxies answer `S_OK`) and the replay waits for
each before the next callback, up to ten seconds, so the engine sees the sequence at its own pace. One `Continue` at
the end releases the stop. Events the agent queued meanwhile are delivered after the replay.

## The protocol client (`RemoteDebuggerClient`)

Requests get a sequence number and a completion; a reader task matches responses to them and queues events into a
channel, whose dispatch starts with the first subscriber and runs on one thread of the pool, so a handler may send
requests of its own. `Invoke` returns the callee's HRESULT and the raw out values, which the caller reads into its
arguments. `Release` gathers the (handle, count) pairs given back for 20 ms and sends them in one request: the proxies
are collected in bursts. A lost connection fails every pending request and every later one at once, and raises
`Disconnected`.

## The session (`RemoteSession`)

One proxy per handle, held weakly, so the engine's identity-keyed dictionaries see the same object every time and a
proxy the engine let go of returns its hand-outs when it is collected. A value or a frame comes in several kinds that
the engine tells apart by casting; the request that brings such an object carries the probe interfaces (values:
generic, reference, handle, heap, object, string, array, box, exception object, delegate object; frames: IL, native,
internal, runtime-unwindable), and the session picks the proxy class from the answer: a string value, an array, a box,
an exception or delegate object, an object, a struct (object with generic bytes), a generic value, a handle, a
reference; a managed frame (IL and native), an IL, native, internal or runtime-unwindable frame. A handle that arrives
without probes (a callback argument) is asked about with a `Query` first, several at once. Every other interface has
one proxy class, chosen by the generated `CreateUniqueProxy`.

## The proxy base (`RemoteObject`)

A proxy holds its session and handle and the count of hand-outs it received; the finalizer gives the count back and
frees the blobs. `Invoke(iid, slot, arguments)` is one request; the helpers `InvokeUInt32`, `InvokeUInt64`,
`InvokeBool`, `InvokeObject<T>` and `InvokeText` cover the common shapes, and `NotProxied` answers `E_NOTIMPL` with a
log line. `InvokeObject<T>` sends the probes of `T`; `FillProxies` makes the proxies of an out array. A blob the callee
pointed into its own memory (a signature, a custom attribute, a constant) is copied into memory the proxy keeps for as
long as it lives (`KeepBlob`), the way the callee keeps its own. A lost connection reads as
`CORDBG_E_PROCESS_TERMINATED`.

What cannot change is asked once. The generator writes a remembering getter for the methods listed in
`Generator/ImmutableResults.cs` (a type's element type, class, base, rank and first parameter; a class's and a
function's module and token; a module's base address, size, token and assembly; a frame's function and code; a code
object's address and size; thread and process ids): the answer of the first successful call is kept in the proxy.
The session keeps the proxies of what lives as long as the process (app domains, assemblies, modules, importers,
functions, classes, types, code) strongly, so those answers survive the debugger letting go of the object between two
stops; everything else (values, frames, chains, steppers, evals) is held weakly and returns its handle when collected.
On an Android device over `adb` this took the calls of a session with one locals page from 4,528 to 3,127 and the
page of 50 locals from 4.4 to 3.0 seconds; what remains is mostly the function evaluations behind the displayed values.

Names are asked for twice by the engine, once for the length and once with a buffer of that length. A call with a text
buffer of capacity zero goes out with a buffer of 1024 characters instead, and its whole response is kept by the call
(slot and in-arguments); the second call, and any repeat, is answered from it without a round trip as long as the
name fit. A longer name is asked for again with the right buffer.

## The proxies (`Proxy/`)

One `[GeneratedComClass] partial class` per kind of object, naming the CorApi interfaces the object has: process, app
domain, assembly, module, thread, function, code, class, type, function breakpoint, stepper, eval, chain, the frames,
the values, the enumerators, the exception call stack enumerator, and the metadata importer (`IMetaDataImport`, `2`,
`IMetaDataTables`, `2`). The generator writes their constructors and methods; a method written by hand in the class
wins. The hand-written ones:

| Class | Method | Why |
|---|---|---|
| `RemoteCorDebugProcess` | `Continue`, `Detach`, `Terminate` | held during a replay; closes the connection; the agent's own request |
| `RemoteCorDebugAppDomain` | `Continue` | held during a replay |
| `RemoteCorDebugModule` | `GetName`, `IsInMemory`, `GetMetaDataInterface` | the device path mapped to the local copy (README.md); a mapped module is a file; the importer proxied as `IMetaDataImport` |
| `RemoteCorDebugType` | `EnumerateTypeParameters` | the parameters are read once and enumerated by a local `LocalCorDebugTypeEnum` |
| `RemoteMetaDataImport` | `GetCustomAttributeByName` | each (token, attribute) lookup is asked once: the display, browsable and proxy attributes of everything shown |
| `RemoteValueBytes` (generic and struct values) | `GetValue`, `SetValue` | the value's bytes through the engine's own buffer, of the size the value reports once |
| `RemoteCorDebugExceptionObjectCallStackEnum` | `Next` | records with a module pointer, carried as `OutRecords` |

## The generator (`Generator/`)

A Roslyn incremental generator the host project references as an analyzer. For every `[GeneratedComClass]` partial
class deriving from `RemoteObject` it reads the class's CorApi interfaces, a base before the interfaces deriving from
it, and writes a method for each interface method the class or a base class does not implement by hand, plus the
`Iids` table (the id of every interface proxied or passed) and the session's `CreateUniqueProxy`. The interfaces are
read from the CorApi **source** files, which the host project lists as `AdditionalFiles`, because `[In]`, `[Out]`,
`[PreserveSig]` and the buffer sizes are pseudo-attributes the compiler folds into metadata flags, where symbols no
longer show them; the compilation is used for the kinds of the parameter types (interface, enum, token, struct) and
for finding the hand-written implementations. The vtable slot is three plus the base interfaces' methods plus the
method's index in its interface.

Each parameter maps to an argument kind by its type and direction:

| Parameter | Request argument | Result |
|---|---|---|
| `uint`, `int`, `bool`, enum, token | `UInt32` | `OutUInt32`, converted back |
| `ulong`, `nuint`, `CordbAddress` | `UInt64` | `OutUInt64`, converted back |
| an interface | `Object(handle, iid)` | `OutObject(probes of the type)`, made a proxy |
| an array of interfaces | `Objects` | `OutObjects(count, probes)`, filled with proxies |
| `byte[]`, arrays of values, structs | `Bytes` | `OutBytes`, copied back or read as the value |
| `string` | `Text` | |
| `ref Guid` | `Guid` | `OutGuid` |
| `ref HCorEnum` | `RefUInt64` | the value back |
| (`cch`, `out pcch`, `char[]`) and (`char[]`, `cch`, `out pch`) | `OutText`, `OutTextBuffer` | the length and the text copied into the buffer |
| (`out nint`/`out byte*`, `out uint`) | `OutBlob` | the bytes kept by the proxy, the pointer to them and the count |
| `byte*` with a `cb` count | `Bytes` of that memory | |

A method with a parameter none of these fits (a context, a register set, a value breakpoint, an edit-and-continue
snapshot, a `sbyte*` name, a metadata row) is written as an `E_NOTIMPL` answer through `NotProxied`, with a comment
naming the parameter. Two interfaces of one class declaring the same method (the IL and native frames' `SetIP` and
`CanSetIP`) are implemented explicitly for each. A method returning `void` is invoked without a result.

## Module names and the assemblies path

The session splits the launch's assemblies path (`;` separated) into folders. `RemoteCorDebugModule.GetName` reports
the device's path when it exists here, else the first file of that name in the folders, with `.dll` appended for a
module the runtime named by its simple name (a module it received as bytes); a module mapped to a local file is
reported as not in memory, so the engine reads the file. The metadata importer is the device's, proxied.

## Logging

`HostLog.Write` is compiled out of a Release build (`[Conditional("DEBUG")]`). A Debug build appends to
`clrdbg-remote-host.log` in the temp folder, or the file `CLRDBG_REMOTE_HOST_LOG` names: the connection and `Hello`,
the callbacks continued by the host, the replay, the calls that failed, and every `NotProxied` method the engine asked
for.
