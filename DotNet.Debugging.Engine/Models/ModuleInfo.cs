using System.Diagnostics;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Metadata;

namespace DotNet.Debugging.Engine.Models;

public class ModuleInfo : IDisposable {
    public string Path { get; }
    public string Name { get; }
    public bool IsUserCode { get; }
    // A module the debuggee emitted at run time (Reflection.Emit, which is how mocking libraries build their
    // proxies): it has no file, no image, no base address, and gains metadata with every type defined in it
    public bool IsDynamic { get; }
    // The file version, falling back to the assembly version from metadata
    public Version? Version { get; }
    public bool HasSymbols => MetadataReader.HasSymbols;
    // The external PDB the symbols were read from, null for embedded or missing symbols
    public string? SymbolFilePath => MetadataReader.SymbolFilePath;

    internal ICorDebugModule Module { get; }
    internal ModuleMetadataReader MetadataReader { get; private set; }
    // Tells the modules of a session apart; a base address could not, dynamic modules have none
    internal int Id { get; }

    internal ModuleInfo(int id, ICorDebugModule module, string path, ModuleMetadataReader metadataReader, bool isUserCode) {
        Id = id;
        Module = module;
        MetadataReader = metadataReader;
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        IsUserCode = isUserCode;
        IsDynamic = module.IsDynamic();
        Version = GetFileVersion(path) ?? metadataReader.GetAssemblyVersion();
    }

    // A dynamic module is re-read whenever a type gets defined in it. The previous reader is not disposed: it
    // owns no unmanaged memory and stays valid for whatever still reads through it
    internal void UpdateMetadata(ModuleMetadataReader metadataReader) {
        MetadataReader = metadataReader;
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
