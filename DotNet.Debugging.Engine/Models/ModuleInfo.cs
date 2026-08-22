using System.Diagnostics;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Metadata;

namespace DotNet.Debugging.Engine.Models;

public class ModuleInfo : IDisposable {
    public string Path { get; }
    public string Name { get; }
    public bool IsUserCode { get; }
    // The file version, falling back to the assembly version from metadata
    public Version? Version { get; }
    public bool HasSymbols => MetadataReader.HasSymbols;
    // The external PDB the symbols were read from, null for embedded or missing symbols
    public string? SymbolFilePath => MetadataReader.SymbolFilePath;

    internal ICorDebugModule Module { get; }
    internal ModuleMetadataReader MetadataReader { get; }
    internal CordbAddress BaseAddress { get; }

    internal ModuleInfo(ICorDebugModule module, string path, ModuleMetadataReader metadataReader, bool isUserCode) {
        Module = module;
        MetadataReader = metadataReader;
        BaseAddress = module.GetBaseAddress();
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        IsUserCode = isUserCode;
        Version = GetFileVersion(path) ?? metadataReader.GetAssemblyVersion();
    }

    public void Dispose() {
        MetadataReader.Dispose();
    }

    private static Version? GetFileVersion(string path) {
        try {
            if (!File.Exists(path))
                return null;
            return Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out var version) ? version : null;
        }
        catch {
            return null;
        }
    }
}
