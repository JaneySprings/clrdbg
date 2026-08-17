namespace DotNet.Debugging.Common.Mobile;

/// <summary>
/// Locates the native remote-debugging binaries that the CoreCLR mobile debugger pipeline needs:
/// the on-target profiler (<c>libvsdbgremotecoreclrtarget</c>) that is injected into the app, and the
/// host-side remote mscordbi host directory. These ship with the Microsoft MAUI / C# Dev Kit tooling
/// and are not redistributable, so the resources root is configurable rather than hard-coded.
///
/// Resolution order for the root:
///   1. the explicit path supplied on the launch configuration (e.g. "vsdbgRemoteResources")
///   2. the VSDBG_REMOTE_RESOURCES environment variable
///
/// The root is expected to contain the layout the MAUI extension ships:
///   &lt;root&gt;/VsdbgRemoteCoreclrTarget/&lt;platform&gt;/&lt;rid&gt;/libvsdbgremotecoreclrtarget.dylib
///   &lt;root&gt;/VsdbgRemoteCoreclrHost/&lt;host-rid&gt;/libvsdbgremotecoreclrhost.dylib
/// </summary>
public static class VsdbgRemoteResources {
    /// <summary>The CoreCLR profiler CLSID that the target-side remote debugger transport registers under.</summary>
    public const string ProfilerGuid = "{9DC623E8-C88F-4FD5-AD99-77E67E1D9631}";

    public const string TargetProfilerFileName = "libvsdbgremotecoreclrtarget.dylib";
    private const string EnvironmentVariable = "VSDBG_REMOTE_RESOURCES";

    /// <summary>
    /// Resolves the resources root from the explicit override or the environment variable.
    /// </summary>
    public static string ResolveRoot(string? explicitRoot = null) {
        if (!string.IsNullOrWhiteSpace(explicitRoot)) {
            if (!Directory.Exists(explicitRoot))
                throw new DirectoryNotFoundException($"vsdbg remote resources path does not exist: {explicitRoot}");
            return explicitRoot;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) {
            if (!Directory.Exists(fromEnvironment))
                throw new DirectoryNotFoundException($"{EnvironmentVariable} points to a path that does not exist: {fromEnvironment}");
            return fromEnvironment;
        }

        throw new InvalidOperationException(
            $"Could not locate the vsdbg remote debugging resources. Set the '{EnvironmentVariable}' environment variable " +
            "or the launch configuration 'vsdbgRemoteResources' property to the directory that contains " +
            "'VsdbgRemoteCoreclrTarget' and 'VsdbgRemoteCoreclrHost' (shipped with the .NET MAUI tooling).");
    }

    /// <summary>
    /// The on-target profiler dylib for a given platform family (e.g. "ios", "maccatalyst") and runtime identifier
    /// (e.g. "maccatalyst-arm64", "iossimulator-arm64").
    /// </summary>
    public static FileInfo TargetProfiler(string platformFamily, string runtimeIdentifier, string? explicitRoot = null) {
        var root = ResolveRoot(explicitRoot);
        var path = Path.Combine(root, "VsdbgRemoteCoreclrTarget", platformFamily, runtimeIdentifier, TargetProfilerFileName);
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException($"Could not find the on-target remote debugger profiler for {platformFamily}/{runtimeIdentifier}", path);
        return file;
    }

    /// <summary>
    /// The host-side remote mscordbi that the debugger loads to drive the remote target (passed to dbgshim as
    /// szMscordbiPath). It lives in the host directory and is matched to the machine running the debugger.
    /// </summary>
    public static FileInfo RemoteMscordbiHost(string? explicitRoot = null) {
        var host = HostDirectory(explicitRoot);
        var file = new FileInfo(Path.Combine(host.FullName, "libremotemscordbihost.dylib"));
        if (!file.Exists)
            throw new FileNotFoundException($"Could not find the remote mscordbi host: {file.FullName}");
        return file;
    }

    /// <summary>
    /// The host-side remote mscordbi host directory, matched to the machine running the debugger.
    /// Passed to dbgshim as part of the assemblies search path.
    /// </summary>
    public static DirectoryInfo HostDirectory(string? explicitRoot = null) {
        var root = ResolveRoot(explicitRoot);
        var hostRid = RuntimeInfo.IsAarch64 ? "osx-arm64" : "osx-x64";
        var path = Path.Combine(root, "VsdbgRemoteCoreclrHost", hostRid);
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
            throw new DirectoryNotFoundException($"Could not find the remote mscordbi host directory for {hostRid}: {path}");
        return directory;
    }
}
