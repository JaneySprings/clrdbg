# DotNet.Debugging.Remote

Remote debugging of CoreCLR apps on Mac Catalyst, the iOS simulator, iOS devices and Android: a library the app loads,
and a library the debugger loads, with a generic handle-and-call protocol between them. The engine debugs the app
through the same `ICorDebug` interfaces it uses for a local process. `docs/remote` describes how it works.

- `Target/` - the device library, `libremotecoreclrtarget`, plain C++. Loaded into the app through the runtime's
  profiler hook before managed code runs, it creates an `ICorDebug` from the app's own `libmscordbi`, attaches it to
  the app's own process and serves the debugger over TCP: a handle table, one generic call, the callbacks as events.
  `Target/build-apple.sh [--enable-trace] [rid...]` builds it for Mac Catalyst, the iOS simulators and the iOS device
  into `<repository>/out/remote-target/<platform>/<rid>/`; `Target/build-android.sh [--enable-trace] [abi...]` builds
  `android/arm64-v8a` and `android/x86_64` with the NDK under `$ANDROID_HOME/ndk` (or `ANDROID_NDK_HOME`).
  `--enable-trace` compiles the trace in: the app's standard output on Apple platforms, logcat on Android.
- `Host/` - the host library, `remotecoreclrhost`, C# published with NativeAOT as a self-contained, trimmed shared
  library: `dotnet publish DotNet.Debugging.Remote/Host -r <rid> -c Release` writes
  `<repository>/out/remote-host/<rid>/libremotecoreclrhost.dylib` (`.so` on Linux, `remotecoreclrhost.dll` on
  Windows). It exports `CreateRemoteCordbObject` and returns an `ICorDebug` whose objects are proxies of the objects in
  the app, one request per method. A Debug build logs to `clrdbg-remote-host.log` in the temp folder.
- `Generator/` - the Roslyn source generator the host project references as an analyzer. For every proxy class it
  writes the methods of the class's CorApi interfaces from the CorApi source files, so a proxy in `Host/Proxy` is one
  declaration naming its interfaces; `dotnet build DotNet.Debugging.Remote/Host -p:EmitCompilerGeneratedFiles=true`
  writes the output under `Host/obj`.

Point a launch's `remoteCoreclrHost` at `out/remote-host` and `remoteCoreclrTarget` at `out/remote-target`; the
launch's `assetsPath` names the local copy of the app's assemblies (`docs/remote/README.md`).
