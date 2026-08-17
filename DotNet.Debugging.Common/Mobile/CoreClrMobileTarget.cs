namespace DotNet.Debugging.Common.Mobile;

/// <summary>
/// A fully resolved CoreCLR mobile debug target: every path and platform string the launcher and the
/// dbgshim remote-attach need, derived from the high-level launch configuration (program dll, platform,
/// runtime identifier, device). Produced by <see cref="Resolve"/>, which validates that the built .app
/// bundle and the required native binaries are present before any launch is attempted.
/// </summary>
public sealed class CoreClrMobileTarget {
    /// <summary>"ios" or "maccatalyst".</summary>
    public required string PlatformFamily { get; init; }
    public required string RuntimeIdentifier { get; init; }
    /// <summary>"arm64" or "x64".</summary>
    public required string Architecture { get; init; }
    public required bool IsMacCatalyst { get; init; }
    public required bool IsSimulator { get; init; }
    /// <summary>The simulator/device UDID (iOS only).</summary>
    public string? DeviceUdid { get; init; }

    /// <summary>Absolute path to the built <c>.app</c> bundle.</summary>
    public required string AppBundlePath { get; init; }
    /// <summary>Absolute path to the native Mach-O executable inside the bundle (maccatalyst launch).</summary>
    public required string BundleExecutablePath { get; init; }
    /// <summary>
    /// Absolute path to the host-side remote mscordbi that the debugger loads to drive the target. This is NOT the
    /// on-device libmscordbi from the app bundle - it is the osx remote host shipped alongside VsdbgRemoteCoreclrHost.
    /// </summary>
    public required string MscordbiPath { get; init; }
    /// <summary>Platform string dbgshim expects, e.g. "maccatalyst;arm64".</summary>
    public required string DbgShimPlatform { get; init; }
    /// <summary>';'-separated assemblies/symbols + remote-host search path for dbgshim.</summary>
    public required string AssembliesPath { get; init; }

    /// <summary>The on-target profiler as shipped by the MAUI tooling (source to copy into the bundle).</summary>
    public required FileInfo ProfilerSource { get; init; }
    /// <summary>Where the profiler must live inside the bundle so the sandboxed app can dlopen it.</summary>
    public required string ProfilerDestinationPath { get; init; }

    public static CoreClrMobileTarget Resolve(
        string programPath,
        string platformFamily,
        string runtimeIdentifier,
        bool isSimulator,
        string? deviceUdid,
        string? resourcesRoot = null) {
        if (string.IsNullOrWhiteSpace(programPath))
            throw new ArgumentException("The launch configuration 'program' (path to the app assembly) is required for mobile debugging.");

        var appDirectory = Path.GetDirectoryName(programPath)
            ?? throw new InvalidOperationException($"Could not determine the output directory from program path: {programPath}");
        var appName = Path.GetFileNameWithoutExtension(programPath);
        var appBundlePath = Path.Combine(appDirectory, appName + ".app");
        if (!Directory.Exists(appBundlePath))
            throw new DirectoryNotFoundException($"The app bundle was not found. Build the app first. Expected: {appBundlePath}");

        var isMacCatalyst = platformFamily.Equals("maccatalyst", StringComparison.OrdinalIgnoreCase);
        var architecture = runtimeIdentifier.Contains('-') ? runtimeIdentifier[(runtimeIdentifier.LastIndexOf('-') + 1)..] : "arm64";

        var bundleExecutablePath = isMacCatalyst ? ResolveMacCatalystExecutable(appBundlePath, appName) : Path.Combine(appBundlePath, appName);

        var profilerSource = VsdbgRemoteResources.TargetProfiler(platformFamily, runtimeIdentifier, resourcesRoot);
        var hostDirectory = VsdbgRemoteResources.HostDirectory(resourcesRoot);
        var mscordbiPath = VsdbgRemoteResources.RemoteMscordbiHost(resourcesRoot).FullName;
        // dbgshim parses this list with ';' (matching the vsdbg 'assetsPath'), regardless of the OS path separator.
        var assembliesPath = string.Join(';', appBundlePath, hostDirectory.FullName);

        // The profiler must live inside the sandbox so the app can dlopen it: maccatalyst apps use
        // Contents/MonoBundle, plain iOS apps use the bundle root.
        var profilerDirectory = isMacCatalyst ? Path.Combine(appBundlePath, "Contents", "MonoBundle") : appBundlePath;
        var profilerDestinationPath = Path.Combine(profilerDirectory, VsdbgRemoteResources.TargetProfilerFileName);

        return new CoreClrMobileTarget {
            PlatformFamily = platformFamily,
            RuntimeIdentifier = runtimeIdentifier,
            Architecture = architecture,
            IsMacCatalyst = isMacCatalyst,
            IsSimulator = isSimulator,
            DeviceUdid = deviceUdid,
            AppBundlePath = appBundlePath,
            BundleExecutablePath = bundleExecutablePath,
            MscordbiPath = mscordbiPath,
            DbgShimPlatform = $"{platformFamily};{architecture}",
            AssembliesPath = assembliesPath,
            ProfilerSource = profilerSource,
            ProfilerDestinationPath = profilerDestinationPath
        };
    }

    private static string ResolveMacCatalystExecutable(string appBundlePath, string appName) {
        // var macOsDirectory = Path.Combine(appBundlePath, "Contents", "MacOS");
        // var infoPlist = Path.Combine(appBundlePath, "Contents", "Info.plist");
        // var executableName = appName;
        // if (File.Exists(infoPlist)) {
        //     // var extractor = new PropertyExtractor(infoPlist);
        //     var cfBundleExecutable = extractor.Extract("CFBundleExecutable");
        //     extractor.Free();
        //     if (!string.IsNullOrWhiteSpace(cfBundleExecutable)) executableName = cfBundleExecutable;
        // }
        // var executablePath = Path.Combine(macOsDirectory, executableName);
        // if (!File.Exists(executablePath))
        //     throw new FileNotFoundException($"Could not find the maccatalyst executable inside the bundle: {executablePath}");
        // return executablePath;
        return string.Empty;
    }
}
