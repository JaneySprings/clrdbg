# The wire protocol

TCP between the device library (the agent, in the app) and the host library (in the debugger). Protocol version 4.
All integers are little-endian. A string is a 16-bit byte count followed by UTF-8; a wide string is a 32-bit character
count followed by UTF-16 code units (the runtime's `WCHAR`); a GUID is its 16 bytes as the runtime lays them out.

## Roles and lifetime

The launcher decides who connects to whom with `CORECLR_REMOTE_DEBUGGER_ISSERVER`: `0` and the app connects out to
`CORECLR_REMOTE_DEBUGGER_IP:PORT`, retrying for 30 seconds; `1` and the app listens on the port (on the loopback
interface when the address is a loopback address, else on every interface) for 30 seconds. Either way the host is
reached before the attach, so the first callbacks find it. Without a host in time the agent attaches anyway; an app
that listens goes on listening and serves the next host (see Hello), an app that connected out does not connect again.

One host at a time. Frames go both ways on the one connection: the host sends requests and reads responses matched by
sequence; the agent sends events on its own. When the connection drops the agent continues the process until it runs
(one `Continue` per stop the host left behind) and releases every handle.

## Framing

```
frame      uint32 length         of everything after this field
           uint8  kind           1 request, 2 response, 3 event
           uint32 sequence       request/response correlation, 0 for an event
request    uint16 command, arguments             host  -> agent
response   int32  hresult, result                agent -> host, same sequence as the request
event      uint16 event, data                    agent -> host
```

A frame longer than 16 MB or shorter than its header is malformed and ends the connection.

## Requests

| Command | Arguments | Result |
|---|---|---|
| 1 Hello | none | `uint32` protocol version, `uint32` pid, string runtime directory, `uint32` process handle: 0 when the attach callbacks are coming to this host, else a handle of the debugged process for the host to replay the attach from |
| 2 Invoke | `uint32` handle, `GUID` interface, `uint16` vtable slot, `uint8` argument count, the arguments | the callee's HRESULT in the response header, then the out value of every argument that has one, in argument order (zeroed when the call failed) |
| 3 Release | any number of (`uint32` handle, `uint32` count) pairs | none: gives that many hand-outs of each handle back |
| 4 Terminate | `uint32` exit code | none; answered before the agent ends its process through `ICorDebugController::Terminate` (or `_exit` if that is refused), the connection drops right after |
| 5 Query | `uint32` handle count, `uint8` interface count, the handles, the `GUID`s | one byte per handle and interface, in that order, 1 when the object has the interface |

An Invoke on an unknown handle answers `E_HANDLE`, on an object that lacks the interface `E_NOINTERFACE`, and a
malformed request `E_INVALIDARG`; anything else is the callee's own HRESULT. A request the agent does not know
answers `E_NOTIMPL`.

## The arguments of Invoke

Each argument is a `uint8` kind followed by its payload. The agent expands the kinds into the words of the native
call in order, makes the call, and writes the out values back in the same order:

| Kind | Payload in the request | Native words | Out value in the response |
|---|---|---|---|
| 1 UInt32 | `uint32` | the value | |
| 2 UInt64 | `uint64` | the value | |
| 3 Object | `uint32` handle, `GUID` interface | the object queried for that interface (NULL for handle 0) | |
| 4 OutUInt32 | | pointer to a `uint32` | `uint32` |
| 5 OutUInt64 | | pointer to a `uint64` | `uint64` |
| 6 OutObject | `uint8` probe count, that many `GUID`s | pointer to an interface pointer | `uint32` handle (0 for NULL), then one byte per probe: 1 when the object has that interface |
| 7 OutText | `uint32` capacity | `ULONG32 cch`, `ULONG32* pcch`, `WCHAR*` buffer (NULL when the capacity is 0) | `uint32` length the callee reported, wide string of what fit |
| 8 Bytes | `uint32` length, bytes | pointer to the bytes | |
| 9 OutBytes | `uint32` length | pointer to a zeroed buffer | `uint32` length, bytes |
| 10 Objects | `uint32` count, `GUID` interface, handles | pointer to an array of interface pointers | |
| 11 OutObjects | `uint32` capacity, `uint8` probe count, `GUID`s | pointer to an array of that many interface pointers | `uint32` count, then per element `uint32` handle and its probe bytes |
| 12 Guid | 16 bytes | pointer to the GUID | |
| 13 Text | wide string | pointer to a NUL-terminated `WCHAR` string | |
| 14 OutTextBuffer | `uint32` capacity | `WCHAR*` buffer (NULL when the capacity is 0), `ULONG cch`, `ULONG* pch`: the metadata API's order of the same pattern | `uint32` length the callee reported, wide string of what fit |
| 15 RefUInt64 | `uint64` | pointer to a `uint64` holding the value, an in/out parameter such as an `HCORENUM` | `uint64` |
| 16 OutBlob | `uint32` unit, `uint32` minimum | pointer to a pointer, pointer to a `ULONG` count: a signature, custom attribute or constant the callee points into its own memory | `uint32` count as the callee reported it, `uint32` byte length, the bytes: count times unit, at least minimum when the pointer is set |
| 17 OutGuid | | pointer to a `GUID` | 16 bytes |
| 18 OutRecords | `uint32` count, `uint32` record size, `uint8` pointer count, that many `uint32` offsets | pointer to a zeroed buffer of count times record size bytes | `uint32` byte length, the bytes, with the interface pointer at each offset of each record replaced by its handle as a 64-bit value (0 for NULL) |

Integers travel as full words: on the platforms the agent runs on, a callee reads the low 32 bits of a register or
stack slot for a 32-bit parameter, so `DWORD`, `BOOL`, enums and metadata tokens are all `UInt32`, and `CORDB_ADDRESS`,
`ULONG64` and `SIZE_T` are `UInt64`. A call takes up to 20 words (`IMetaDataImport::GetPropertyProps` takes 17).

The probes of `OutObject` and `OutObjects` are how the host tells the kinds of values and frames apart without a
second round trip: the request names the interfaces in question, the response says which the returned object has.
`Query` does the same for handles that arrive without probes, the arguments of a callback. Records (`OutRecords`)
carry the frames of `ICorDebugExceptionObjectCallStackEnum::Next`, whose module pointers come back as handles; a
record the callee did not fill stays zero, so it yields handle 0.

## Handles

COM identity is the `IUnknown` pointer, so the same object always gets the same handle and the host keeps one proxy
per object, which is what the engine's identity-keyed dictionaries need. The table holds one reference per object.
Every hand-out (an out object of a call, an argument of a callback, the process handle of a Hello) counts one; the
host's proxy gives its count back with `Release` when the debugger lets go of it, the host gives back at once the
hand-outs of a callback it does not deliver, and a disconnect releases everything.

## Events

| Event | Data | Meaning |
|---|---|---|
| 1 Callback | `uint16` callback id, then the callback's arguments in their order: `uint32` handle for an object, `uint32` for an integer, enum or `BOOL`, wide string for a `WCHAR*` | An `ICorDebugManagedCallback` or `ICorDebugManagedCallback2` callback fired; the process stays stopped until the host continues the controller (the app domain or process the callback names, the process for a `NameChange` without an app domain) |

The callback ids follow the vtable order: 1 Breakpoint, 2 StepComplete, 3 Break, 4 Exception, 5 EvalComplete,
6 EvalException, 7 CreateProcess, 8 ExitProcess, 9 CreateThread, 10 ExitThread, 11 LoadModule, 12 UnloadModule,
13 LoadClass, 14 UnloadClass, 15 DebuggerError, 16 LogMessage, 17 LogSwitch, 18 CreateAppDomain, 19 ExitAppDomain,
20 LoadAssembly, 21 UnloadAssembly, 22 ControlCTrap, 23 NameChange, 24 UpdateModuleSymbols (the symbol stream is
dropped), 25 EditAndContinueRemap, 26 BreakpointSetError; `ICorDebugManagedCallback2`: 27 FunctionRemapOpportunity,
28 CreateConnection, 29 ChangeConnection, 30 DestroyConnection, 31 Exception2, 32 ExceptionUnwind,
33 FunctionRemapComplete, 34 MDANotification. The handles of a callback come first, then its numbers and texts.
`ExitProcess` is the one callback that leaves nothing stopped.
