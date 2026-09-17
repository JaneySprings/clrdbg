# The device library

`Target/` builds `libremotecoreclrtarget`, the library the app loads. Plain C++: functions and structs, `std::string`
and `std::vector`, a `std::mutex` with a `lock_guard` where two threads meet, no templates of its own. One source per
concern:

| File | Content |
|---|---|
| `Com.h`, `Com.cpp` | the little COM the library needs: the runtime's types (`HRESULT`, 16-bit `WCHAR`, `GUID`), the interface ids, `QueryInterface`/`AddRef`/`Release` through a vtable, and `CallMethod` |
| `Profiler.cpp` | the class factory and the profiler callback object the runtime asks for; `DllGetClassObject`, `DllCanUnloadNow` |
| `Agent.cpp` | the agent thread: the `ICorDebug` from the app's own `libmscordbi`, the connection, the attach |
| `Protocol/` | the wire types (`ByteReader.h`, `ByteWriter.h`), the constants (`Protocol.h`), the TCP connection (`Connection.cpp`) |
| `Debugger/Handles.cpp` | the handle table |
| `Debugger/Invoke.cpp` | the `Invoke` and `Query` requests |
| `Debugger/Callbacks.cpp` | the `ICorDebugManagedCallback` and `ICorDebugManagedCallback2` objects, reported as events |
| `Debugger/Requests.cpp` | the request loop of a connected host, `Hello`, `Release`, `Terminate` |
| `Log.cpp` | the trace, compiled in with `--enable-trace` only |

## Loading

The runtime loads the library through its profiler hook (`CORECLR_ENABLE_PROFILING`, `CORECLR_PROFILER`,
`CORECLR_PROFILER_PATH`) before any managed code runs, and the runtime's debugger is initialized before that, which is
why the agent finds a debugger transport to attach to. `DllGetClassObject` hands out a class factory whose
`CreateInstance` yields an `ICorProfilerCallback2` with the interface's 110 slots; every slot but `Initialize` answers
`S_OK` and does nothing, no event mask is set, so no other profiler callback ever fires. `Initialize` starts the agent
thread (a detached pthread) and returns. The library is not a profiler and profiles nothing.

## Creating the debugger (`Agent.cpp`, `CreateDebugger`)

1. The runtime's folder is the folder of `libcoreclr`, found by the address of its exported `coreclr_initialize`:
   `dlsym(RTLD_DEFAULT)` finds it where the loader made the runtime global (Apple); Android's loader keeps a `dlopen`ed
   library private, so there `dlopen("libcoreclr.so", RTLD_NOLOAD)` gives the loaded library to look in. `dladdr` gives
   the path and the base address.
2. `libmscordbi` and `libmscordaccore` are looked for flat in that folder (`lib<name>.dylib` or `.so`) and, failing
   that, as frameworks next to it (`lib<name>.framework/lib<name>`), the two layouts the runtime packs use.
3. `libmscordbi` is `dlopen`ed and its exported `DllMain(NULL, DLL_PROCESS_ATTACH, NULL)` is called by hand: the
   runtime's own loader does that after every `dlopen`, and it is where the library stands up the PAL it shares with
   `libmscordaccore`; without it the first PAL call crashes.
4. `CoreCLRCreateCordbObject3(CorDebugVersion_2_0, getpid(), NULL, <path of libmscordaccore or NULL>, <base of
   libcoreclr>, &cordb)` makes the `ICorDebug`. The base address is the identity of the runtime instance to debug; the
   data access library's path is passed because `libmscordbi` looks for it in its own folder otherwise, and in the
   framework layout that folder holds `libmscordbi` alone.
5. `ICorDebug::Initialize`, then `SetManagedHandler` with the callback object of `Callbacks.cpp`.

Every method of an `ICorDebug` object is called through `CallMethod(object, slot, words)`: one call shape with 20
machine words, of which the callee reads as many as its parameters take (the caller owns the argument registers and
the stack it pushed, on the arm64 and x86-64 conventions of Apple and Android). That is what lets the agent make any
call the host names without knowing the interfaces.

## Reaching the host and attaching (`AgentMain`)

`CORECLR_REMOTE_DEBUGGER_PORT`, `_IP` and `_ISSERVER` say where the debugger is. With `_ISSERVER=1` the agent listens
on the port (loopback only when the address is a loopback address) and waits up to 30 seconds for a host; otherwise
it connects out to the address, retrying every 250 ms for 30 seconds. Then, with or without a host, it calls
`ICorDebug::DebugActiveProcess(getpid(), FALSE)`: the runtime's debugging library attaches to the process it lives in,
and the callbacks of the attach start on its own event thread. Without a host every callback is continued at once.
With one, the agent thread serves the host's requests until the connection drops; an app that listens then waits for
the next host, forever.

The threads of the library never enter the runtime: the profiler callback thread starts a pthread and returns, the
agent thread and the debugger's event thread run native code only, so the debugger's suspension never catches them.

## Handles (`Handles.cpp`)

Every object handed to the host has a handle, from 1 up. The table is keyed by COM identity (the `IUnknown` pointer,
asked of every object), so the same object always gets the same handle. It holds one reference per object: an object
returned by a call brings its own reference (the table takes it, or releases it when the object is known already), an
argument of a callback is only borrowed (the table adds a reference). Each hand-out counts one; `Release(handle, count)`
gives counts back, and the object is released when its count reaches zero. `ResolveHandle` queries the object for the
interface a request names, so a request on an object that lacks it fails with `E_NOINTERFACE` and one on a handle
already released with `E_HANDLE`.

## Invoke and Query (`Invoke.cpp`)

An `Invoke` names a handle, an interface id, a slot and typed arguments (protocol.md). Each argument is read into a
`CallArgument` (its value, its buffer, the objects it resolved), expanded into native words, and after the call its out
value is written to the response. An empty array of objects travels as a pointer to a static buffer, never as NULL:
the runtime's debugging library refuses a NULL array even with a count of zero (a constructor call without arguments
failed that way), as an in-process caller never passes one. Empty bytes travel as NULL: a NULL signature tells the
metadata importer to match a member by name alone, and a non-NULL empty one matches nothing. After the call an out object is registered and its probe interfaces are answered on the spot, a
text buffer comes back with the length the callee reported, a blob the callee pointed into its own memory is copied,
records get their interface pointers replaced by handles. The interfaces resolved for in-arguments are references of
the request only and are released after the call. A failure of the callee is passed on as its HRESULT with the outs
zeroed; only a call that could not be made (bad handle, missing interface, malformed request) is logged. `Query`
answers, for several handles and interface ids at once, which objects have which interfaces.

## Callbacks (`Callbacks.cpp`)

Two static objects implement `ICorDebugManagedCallback` and `ICorDebugManagedCallback2` (`SetManagedHandler` queries
the first for the second). Every callback is reported as one event: the callback id, the handles of its object
arguments in order, then its numbers and strings. The callback returns `S_OK` right away and leaves the process
stopped, the way a queued callback does; the host continues the controller when the debugger is done. Without a host
the callback continues the controller itself (the process for a `NameChange` without an app domain) and registers
nothing. `ExitProcess` is reported and not continued. `CreateProcess` remembers the process and the host connection
it went to: a `Hello` from another connection later gets a handle of the process, which tells that host to replay the
attach (host.md). The symbol stream of `UpdateModuleSymbols` is not carried.

## A host that leaves (`Requests.cpp`)

When the host's connection ends, by its `Detach` or by a failure, the agent continues the process until it runs
(`IsRunning` false means one stop is left: a callback the host did not continue, or a `Stop` it asked for) and
releases every handle. `Terminate` is answered first and carried out after: `ICorDebugController::Terminate` on the
process, or `_exit` should that be refused.

## Building and tracing

`build-apple.sh [--enable-trace] [rid...]` compiles with clang for each Apple target against its SDK
(`-target <arch>-apple-<os>`, `-dynamiclib`, hidden visibility, `@rpath` install name) and signs the result ad hoc;
the Mac Catalyst targets come first so a missing iOS SDK stops nothing before them. `build-android.sh [--enable-trace]
[abi...]` uses the NDK's clang for API 21 with libc++ linked in, so the app ships no `libc++_shared.so`. Only
`DllGetClassObject` and `DllCanUnloadNow` are exported.

`--enable-trace` defines `CLRDBG_TRACE`, which compiles `Log` in: one line per step and callback to the app's standard
output on Apple platforms (prefixed `[remotecoreclrtarget]`) and to logcat on Android under that tag. Without the
define every `Log` call compiles to nothing.
