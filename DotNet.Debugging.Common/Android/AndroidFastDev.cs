using System.Text;
using DotNet.Debugging.Common.Extensions;
using DotNet.Debugging.Common.Interop;

namespace DotNet.Debugging.Common.Android;

public static class AndroidFastDev {
    public static void TryPushAssemblies(string serial, string? assetsPath, string applicationId, IProcessLogger? logger) {
        assetsPath = assetsPath?.TrimPathEnd();
        if (string.IsNullOrEmpty(assetsPath) || !Directory.Exists(assetsPath)) {
            logger?.OnErrorDataReceived($"[FastDev]: Path '{assetsPath}' is not valid or does not exist.");
            return;
        }
        if (Directory.GetFiles(assetsPath, "*.dll", SearchOption.AllDirectories).Length == 0) {
            logger?.OnErrorDataReceived($"[FastDev]: Skipping push, no assemblies found in '{assetsPath}'");
            return;
        }

        logger?.OnOutputDataReceived($"[FastDev]: Pushing '{assetsPath}' to device...");
        AndroidDebugBridge.Shell(serial, "mkdir", "-p", $"/data/local/tmp/{applicationId}");
        AndroidDebugBridge.Push(serial, assetsPath, $"/data/local/tmp/{applicationId}", logger);

        logger?.OnOutputDataReceived("[FastDev]: Deleting existing assemblies in app directory");
        AndroidDebugBridge.Shell(serial, "run-as", applicationId, "mkdir", "-p", $"/data/user/0/{applicationId}/files"); // Create directory if not exists
        AndroidDebugBridge.Shell(serial, "run-as", applicationId, "rm", "-rf", $"/data/user/0/{applicationId}/files/.__override__"); // Ensure directory is empty

        logger?.OnOutputDataReceived("[FastDev]: Copying assemblies to app directory");
        var assetsName = Path.GetFileName(assetsPath);
        var result = AndroidDebugBridge.ShellResult(serial, "run-as", applicationId, "cp", "-r", $"/data/local/tmp/{applicationId}/{assetsName}", $"/data/user/0/{applicationId}/files/.__override__");
        if (!result.Success)
            logger?.OnErrorDataReceived($"[FastDev]: Failed to copy assemblies to app directory: {result.GetError()}");

        logger?.OnOutputDataReceived("[FastDev]: Cleaning up temporary directory");
        AndroidDebugBridge.Shell(serial, "rm", "-rf", $"/data/local/tmp/{applicationId}");
    }
    public static void TrySetEnvironment(string serial, Dictionary<string, string> environment, string? assetsPath, string applicationId, IProcessLogger? logger) {
        if (environment.Count == 0 || !Directory.Exists(assetsPath))
            return;

        logger?.OnOutputDataReceived($"[FastDev]: Setting {environment.Count} environment variable(s)...");

        var environmentFile = Path.Combine(AppContext.BaseDirectory, $"{applicationId}.environment");
        if (File.Exists(environmentFile))
            File.Delete(environmentFile);
        File.WriteAllBytes(environmentFile, CreateEnvironmentBytes(environment));

        AndroidDebugBridge.Push(serial, environmentFile, $"/data/local/tmp/{applicationId}.environment", logger);
        foreach (var abiPath in Directory.EnumerateDirectories(assetsPath)) {
            var abiName = Path.GetFileName(abiPath);
            var overridePath = $"/data/user/0/{applicationId}/files/.__override__/{abiName}";
            AndroidDebugBridge.Shell(serial, "run-as", applicationId, "mkdir", "-p", overridePath); // Create directory if not exists
            AndroidDebugBridge.Shell(serial, "run-as", applicationId, "rm", "-f", $"{overridePath}/environment");

            var result = AndroidDebugBridge.ShellResult(serial, "run-as", applicationId, "cp", $"/data/local/tmp/{applicationId}.environment", $"{overridePath}/environment");
            if (!result.Success)
                logger?.OnErrorDataReceived($"[FastDev]: Failed to set environment variables for '{abiName}': {result.GetError()}");
        }

        AndroidDebugBridge.Shell(serial, "rm", "-f", $"/data/local/tmp/{applicationId}.environment");
        File.Delete(environmentFile);
        logger?.OnOutputDataReceived($"[FastDev]: Environment variables configured");
    }

    public static string GetAssembliesPath(string? assetsPath) {
        if (!Directory.Exists(assetsPath))
            throw new DirectoryNotFoundException($"Directory not found: {assetsPath}");

        var searchDirectories = new List<string>() { assetsPath };
        searchDirectories.AddRange(Directory.GetDirectories(assetsPath));

        return string.Join(';', searchDirectories.Select(abiPath => {
            if (!abiPath.EndsWith(Path.DirectorySeparatorChar))
                return abiPath + Path.DirectorySeparatorChar; // Very important thing!!!
            return abiPath;
        }));
    }


    private static byte[] CreateEnvironmentBytes(Dictionary<string, string> environment) {
        const int EnvironmentHeaderFieldSize = sizeof(byte) * 11; // '0x' + 8 hex digits + NUL
        const int EnvironmentHeaderSize = EnvironmentHeaderFieldSize * 2;

        var rawDictionary = environment.ToDictionary(it => Encoding.UTF8.GetBytes(it.Key), it => Encoding.UTF8.GetBytes(it.Value));
        var nameWidth = rawDictionary.Max(it => it.Key.Length) + 1;
        var valueWidth = rawDictionary.Max(it => it.Value.Length) + 1;

        // The buffer is zero initialized, which already provides the terminators and the padding
        var block = new byte[EnvironmentHeaderSize + rawDictionary.Count * (nameWidth + valueWidth)];
        Encoding.ASCII.GetBytes($"0x{nameWidth:x8}").CopyTo(block, 0);
        Encoding.ASCII.GetBytes($"0x{valueWidth:x8}").CopyTo(block, EnvironmentHeaderFieldSize);

        var offset = EnvironmentHeaderSize;
        foreach (var record in rawDictionary) {
            record.Key.CopyTo(block, offset);
            record.Value.CopyTo(block, offset + nameWidth);
            offset += nameWidth + valueWidth;
        }
        return block;
    }
}