using DotNet.Debugging.Common.Logging;

namespace DotNet.Debugging.Adapter.Symbols;

public class SymbolsResolver {
    public const string MicrosoftSymbolServerAddress = "https://msdl.microsoft.com/download/symbols";
    public const string NuGetSymbolServerAddress = "https://symbols.nuget.org/download/symbols";

    private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly List<string> searchDirectories;
    private readonly List<string> serverAddresses;
    private readonly string cachePath;
    private readonly CurrentClassLogger logger;

    public bool HasSymbolServers => serverAddresses.Count > 0;

    public SymbolsResolver(SymbolOptions options) {
        logger = new CurrentClassLogger(nameof(SymbolsResolver));
        searchDirectories = new List<string>();
        serverAddresses = new List<string>();
        cachePath = ExpandHomeDirectory(options.CachePath) ?? Path.Combine(AppContext.BaseDirectory, "SymbolsCache");

        foreach (var searchPath in options.SearchPaths) {
            if (searchPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || searchPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                serverAddresses.Add(searchPath.TrimEnd('/'));
            else if (ExpandHomeDirectory(searchPath) is string searchDirectory)
                searchDirectories.Add(searchDirectory);
        }
        if (options.SearchMicrosoftSymbolServer)
            serverAddresses.Add(MicrosoftSymbolServerAddress);
        if (options.SearchNuGetSymbolServer)
            serverAddresses.Add(NuGetSymbolServerAddress);
    }

    // The full path of the module's PDB, null when no configured location has it. The caller verifies
    // the file against the module's signature, a stale PDB in a search path is rejected there
    public string? FindSymbols(string symbolFileName, Guid pdbGuid) {
        foreach (var searchDirectory in searchDirectories) {
            var candidatePath = Path.Combine(searchDirectory, symbolFileName);
            if (File.Exists(candidatePath))
                return candidatePath;
        }
        if (serverAddresses.Count == 0)
            return null;

        // Symbol servers index a portable PDB by its signature GUID plus the 'portable' age marker
        var key = $"{pdbGuid:N}FFFFFFFF";
        var cachedPath = Path.Combine(cachePath, symbolFileName, key, symbolFileName);
        if (File.Exists(cachedPath))
            return cachedPath;

        foreach (var serverAddress in serverAddresses) {
            if (TryDownloadFile($"{serverAddress}/{symbolFileName}/{key}/{symbolFileName}", cachedPath)) {
                logger.Debug($"Symbols '{symbolFileName}' downloaded from {serverAddress}");
                return cachedPath;
            }
        }
        return null;
    }

    private bool TryDownloadFile(string url, string outputFilePath) {
        try {
            using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return false;

            var data = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
            File.WriteAllBytes(outputFilePath, data);
            return true;
        }
        catch (Exception ex) {
            logger.Error($"Failed to download '{url}': {ex.Message}");
            return false;
        }
    }
    private static string? ExpandHomeDirectory(string? path) {
        if (string.IsNullOrEmpty(path))
            return null;
        if (path == "~" || path.StartsWith("~/"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~', '/'));
        return path;
    }
}
