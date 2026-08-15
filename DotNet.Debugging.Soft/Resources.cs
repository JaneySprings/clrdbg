namespace DotNet.Debugging.Soft;

public static class Resources {
    public const string MessageMissingProcess = "Must specify either processId or processName.";
    public const string MessageInvalidProcess = "Failed to attach to process {0}.";
    public const string MessageInvalidProgram = "launch: program '{0}' does not exist.";

    public const string MessageCannotFindPdb = "Cannot find or open the PDB file.";
    public const string MessagePdbLoaded = "Symbols loaded.";
    public const string MessagePdfSkipped = "Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.";
}