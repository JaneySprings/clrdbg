# Remote debugging of CoreCLR apps

`DotNet.Debugging.Remote` lets the engine debug a CoreCLR app running on Mac Catalyst, the iOS simulator, an iOS
device or Android. The engine itself is unchanged: it drives the runtime through the `ICorDebug` interfaces as it does
for a local process. Two libraries stand between it and the app:

- the **device library**, `libremotecoreclrtarget` (`Target/`, C++), which the app loads at startup. It creates an
  `ICorDebug` from the runtime's own debugging library inside the app (`libmscordbi`, shipped in the app's runtime
  pack), attaches it to the app's own process, and serves a debugger over TCP;
- the **host library**, `remotecoreclrhost` (`Host/`, C# compiled with NativeAOT), which the debugger loads. It
  implements `ICorDebug` and hands the engine proxies: every object the engine sees lives in the app, and every method
  the engine calls is one request to the device library.

The protocol between them is generic (protocol.md): a handle per object, a call by interface id and vtable slot, and
the debugger's callbacks as events. The device library knows nothing of the interfaces; the host library, which has the
CorApi definitions, encodes each call and decodes the results, most of it written at build time by a source generator
(host.md). agent.md describes the device library.

| Document | Content |
|---|---|
| [protocol.md](protocol.md) | The wire protocol: framing, requests, argument kinds, handles, events |
| [agent.md](agent.md) | The device library: loading, the attach, the connection, handles, callbacks |
| [host.md](host.md) | The host library: the `ICorDebug`, the proxies and their generator, the session, module names |

## A session, end to end

1. The adapter composes the app's environment: `CORECLR_ENABLE_PROFILING=1`, `CORECLR_PROFILER` (any GUID),
   `CORECLR_PROFILER_PATH` naming the device library inside the app, and `CORECLR_REMOTE_DEBUGGER_IP`, `_PORT` and
   `_ISSERVER` telling the library where the debugger is and who connects to whom. It loads the host library through
   dbgshim's `RegisterForRuntimeStartupRemotePort`, which calls the library's `CreateRemoteCordbObject` export with the
   address, the port, the role and the assemblies path, and gets the `ICorDebug` back.
2. The runtime loads the device library through its profiler hook before any managed code runs. The library is not a
   profiler: its `Initialize` starts one thread and returns.
3. That thread creates the `ICorDebug`, reaches the debugger (it connects out to a listening host, or listens for one,
   as `_ISSERVER` says) and attaches to its own process with `DebugActiveProcess`. The callbacks of the attach follow:
   `CreateProcess`, `CreateAppDomain`, a `LoadAssembly` and `LoadModule` pair per assembly, `CreateThread` per thread.
4. Every callback goes to the host as an event carrying the handles of its arguments, and leaves the process stopped,
   as a callback does. The host hands it to the engine's managed callback with the arguments proxied; the engine
   continues the process when it is done, and the `Continue` goes back as a request on the controller's handle.
5. From then on the engine asks for names, frames, values and metadata through the proxies, sets breakpoints and
   steppers, and runs function evaluations; each proxied method is one round trip. A module's metadata and symbols are
   read from a local copy of the assembly (below), never over the connection.
6. `Detach` closes the connection: the device library continues whatever the host left stopped and releases every
   handle. `Terminate` is a request the device library answers before ending the process.

## Launch settings

| Setting | Meaning |
|---|---|
| `remoteCoreclrHost` | the folder holding `<os>-<arch>/libremotecoreclrhost.dylib` (`.so` on Linux, `remotecoreclrhost.dll` on Windows) |
| `remoteCoreclrTarget` | the folder holding `<platform>/<rid>/libremotecoreclrtarget.dylib` (`android/<abi>/libremotecoreclrtarget.so`); the app build packages the file for its target from here |
| `coreClrMobileDebuggerOptions.platform` | `maccatalyst`, `ios` or `android` |
| `ip`, `port` | the debugger's address and port; port `0` picks a free one |
| `assetsPath` | the folder holding the app's assemblies on the host (next section) |
| `device`, `isDevice` | the simulator or device to run on |
| `tcpTunnel` | further ports forwarded to the device |

Who listens depends on the platform, and follows from how the debugger reaches the app:

| Platform | Who listens | Transport | Where the device library sits |
|---|---|---|---|
| Mac Catalyst | the debugger | the app connects out on the local machine | `<App>.app/Contents/MonoBundle` |
| iOS simulator | the debugger | the app connects out on the local machine | `<App>.app` |
| iOS device | the app | a port tunnelled to the device by the launcher; the debugger connects through it | `<App>.app` |
| Android | the app | `adb forward tcp:<port> tcp:<port>`; the debugger connects through it | the APK's `lib/<abi>/` |

An app that listens binds the loopback interface when `CORECLR_REMOTE_DEBUGGER_IP` is a loopback address, which both
tunnels deliver to, and every interface otherwise. A forwarded port accepts the debugger's connection before the app
listens behind it and drops it right after, so the host counts a connection only once the device library has answered
`Hello`, and keeps trying for two minutes while the app starts.

## Assemblies and symbols on the host

The engine reads a module's metadata and symbols from a local copy of the assembly. `assetsPath` names the folder that
holds those copies, and it has to be what the device runs, byte for byte:

| Target | `assetsPath` |
|---|---|
| Mac Catalyst | `<App>.app/Contents/MonoBundle` |
| iOS simulator and device | `<App>.app` (the assemblies sit at the bundle's root, as on the device) |
| Android | `obj/Debug/net<version>-android/android/assets/`, the folder the adapter fast-deploys; the adapter adds its per-ABI subfolders (`arm64-v8a/`, ...) to the list itself |

The `bin/.../ios-arm64` folder holds the copies from before the SDK's linker ran, and a device build trims the
framework assemblies even in Debug, so `System.Private.CoreLib.dll` there is not the one the device runs and its tokens
would not match.

How the lookup works: the device reports a module by its path on the device. When that path does not exist on the
host (it does on Mac Catalyst and the simulator, whose bundles are local folders), the host library takes the file
name and looks for it in each folder of the assemblies list the adapter composes, `<assetsPath>;<folder of the host
library>`. The path the engine ends up with is the one its log prints as `Module loaded: ...`; a device path there,
followed by `The metadata of X could not be read`, means the file was not found under `assetsPath`, and the module
stays unregistered: no "Loaded" line, no module event, no breakpoints in it.

A module the runtime received as bytes rather than as a file has no path at all. Android's loader hands the runtime
the core library that way: the runtime names the module by its simple name, `System.Private.CoreLib`, and reports it
as in memory (`ICorDebugModule::IsInMemory`). The host library looks such a name up with `.dll` appended and, once the
file is found, reports the module as that file and not in memory, so the engine reads the file like any other instead
of copying the image out of the app.

Symbols follow the assembly: the engine looks for the portable PDB next to the mapped assembly first, then embedded in
it, then in `symbolOptions.searchPaths` and on the symbol servers, checking the PDB's id against the assembly each
time. A Debug bundle normally carries the app's PDB next to its dll; a build that leaves it out gets it from the build
folder listed in `symbolOptions.searchPaths`.

## Platform notes

- **Runtime pack layouts.** Mac Catalyst, the simulators and Android put the runtime's libraries flat next to
  `libcoreclr` (`libmscordbi`, `libmscordaccore`); the iOS device packages each as a framework of its own,
  `Frameworks/libmscordbi.framework/libmscordbi` next to `libcoreclr.framework`, with no extension. The device library
  looks in both places, and passes the path of `libmscordaccore` (the runtime's data access library) to
  `CoreCLRCreateCordbObject3` because `libmscordbi` looks for it in its own folder otherwise, which in the framework
  layout holds `libmscordbi` alone.
- **Android's loader.** A `dlopen`ed library is not visible to `dlsym(RTLD_DEFAULT)`, so the device library asks for
  `libcoreclr.so` by name (`RTLD_NOLOAD`) to find the runtime's address. The assemblies are not in the APK: the
  adapter fast-deploys them to the app's `.__override__` folder, and the loader hands the runtime the core library
  as bytes (previous section).
- **Signatures on Mac Catalyst.** A launch killed with `SIGKILL (Code Signature Invalid)` means one of the executable's
  linked dylibs is refused by the kernel. Two causes met: a signed binary overwritten in place after it was mapped
  once, which leaves the kernel's cached signature stale, so replace files with `rm` + `cp` rather than copying over
  them; and a build that rewrote the install names of the runtime's dylibs without re-signing them. `cmp file file` on
  a suspect dylib exits 137 when the kernel refuses to map it. Re-signing a bundle with `codesign --force --sign -`
  drops its entitlements unless `--entitlements` names the build's `Entitlements.xcent`. A Debug Catalyst app is ad-hoc
  signed and sandboxed with the network client and server entitlements; an ad-hoc signed device library copied into
  `Contents/MonoBundle` loads, and the sandbox permits the TCP listener.
- **A paused idle Android app lists no main thread**: its managed frames have all returned to Java, and the engine
  hides threads without managed frames.

## A host that comes late, or second

The device library waits 30 seconds for the host before it attaches; without one it attaches anyway and the app runs
undebugged, with the attach callbacks continued on the spot. An app that listens keeps listening: a host that connects
afterwards, or after an earlier host disconnected, learns from `Hello` that the attach callbacks are not coming and
rebuilds them from the process: it stops the process, hands the engine `CreateProcess`, the app domain, every assembly
with its modules and every thread, waiting after each for the engine's `Continue` (which it holds rather than forwards),
and continues the process once at the end. The engine sees the sequence it would have seen from the runtime. An app
that connects out (Mac Catalyst, the simulator) makes one connection and does not reconnect after the host is gone.

## Building

- Device library: `Target/build-apple.sh [--enable-trace] [rid...]` builds `maccatalyst-arm64`, `maccatalyst-x64`,
  `iossimulator-arm64`, `iossimulator-x64` and `ios-arm64`, in that order, into
  `out/remote-target/<platform>/<rid>/libremotecoreclrtarget.dylib`, each signed ad hoc;
  `Target/build-android.sh [--enable-trace] [abi...]` builds `arm64-v8a` and `x86_64` with the NDK found under
  `$ANDROID_HOME/ndk` (or `ANDROID_NDK_HOME`) into `out/remote-target/android/<abi>/libremotecoreclrtarget.so`, with
  libc++ linked in so the app ships nothing else. Plain clang, no runtime headers: the little COM the library needs
  is in `Com.h`.
- Host library: `dotnet publish DotNet.Debugging.Remote/Host -r <rid> -c Release` writes
  `out/remote-host/<rid>/libremotecoreclrhost.dylib` (the project file names the file and the folder). The proxies are
  generated by `Generator/`, a Roslyn source generator the host project references as an analyzer;
  `dotnet build DotNet.Debugging.Remote/Host -p:EmitCompilerGeneratedFiles=true` writes the generated files under
  `Host/obj` for a look.
- Point a launch's `remoteCoreclrHost` at `out/remote-host` and `remoteCoreclrTarget` at `out/remote-target`.

## Logging

The device library writes nothing unless built with `--enable-trace`: then it prints one line per step and callback
to the app's standard output on Apple platforms, prefixed `[remotecoreclrtarget]`, and to logcat on Android under that
tag (the adapter's console shows both). A Debug build of the host library writes `clrdbg-remote-host.log` in the
debugger's temp folder (`CLRDBG_REMOTE_HOST_LOG` names another file): the connection, the callbacks it continued
itself, every call the engine makes (proxy, handle, slot, arguments and HRESULT), and every method the engine asked
for that is not proxied; a Release build writes no log. `dotnet publish DotNet.Debugging.Remote/Host -r osx-arm64
-c Debug -p:PublishDir=<folder>/osx-arm64/` puts such a build in a folder of its own, for a launch whose
`remoteCoreclrHost` points there. The engine's own log shows the modules as they are mapped and every callback it
handles.

## What works, and what does not

Verified through the adapter and the engine on the MAUI test app: a breakpoint in the page constructor binds and hits;
the stack lists in 25 ms on Mac Catalyst and 190 ms on an Android device over `adb` (a page of 50 locals with
their display strings takes 0.7 s and 3 s there); locals show `this` with its
DebuggerDisplay and expand; arrays, structs and strings display and expand; assignments, property reads, function
evaluations (`GetType().Name`, `ToString()`, `DateTime.Now`) and constructor calls run in the app; an exception thrown
inside an evaluation is reported; step over lands on the next line; pause stops the running app and lists the threads
with their stacks; a first-chance exception in user code stops with its type and message; disconnect ends the app
through the device library. The iOS simulator, an iOS device and an Android device run the same session; the
full case sweep (variables and formatting, member listing, set variable, evaluation, arrays, slow and throwing
evaluations, stepping and step filtering, hidden regions, set next statement, debug output, conditions, hit counts,
logpoints, function breakpoints, threads, exception stops with recorded traces, async stepping, pause, detach) passes
on Mac Catalyst, the iOS simulator and the Android device. A host connecting after the app
attached alone receives the replayed attach (1 process, 1 app domain, 49 assemblies and modules, 3 threads in 21 ms).

Not proxied, by design of the wire (the host log names each call): thread and chain contexts and register sets, the
register values of native frames, value and module breakpoints, edit-and-continue snapshots, the raw rows, strings
and blobs of the metadata tables, process and thread OS handles, managed copies of values. The engine uses none of
them. The recorded call stack of an exception object is proxied, and is empty at a first-chance stop, before the
runtime has unwound any frame.
