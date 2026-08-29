namespace DotNet.Debugging.Engine.Models;

// Raised for a module whose symbols are not next to it: the host may locate the PDB elsewhere
// (search paths, symbol servers) and hand it back through 'SymbolFilePath'
public class SymbolsRequest {
    public string ModulePath { get; }
    // The PDB file name from the CodeView debug directory, e.g. 'MyLib.pdb'
    public string SymbolFileName { get; }
    // The PDB signature symbol servers key by
    public Guid PdbGuid { get; }
    // Set by the host: the full path of the located PDB, left null when it was not found
    public string? SymbolFilePath { get; set; }

    public SymbolsRequest(string modulePath, string symbolFileName, Guid pdbGuid) {
        ModulePath = modulePath;
        SymbolFileName = symbolFileName;
        PdbGuid = pdbGuid;
    }
}
