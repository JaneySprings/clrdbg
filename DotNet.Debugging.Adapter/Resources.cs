namespace DotNet.Debugging.Adapter;

public static class Resources {
    public const string MessageMissingProcess = "Must specify either processId or processName.";
    public const string MessageNoRunningProcesses = "No process with the specified name is currently running.";
    public const string MessageMultipleProcesses = "Multiple processes were found matching the process name. Attach by process id instead.";
    public const string MessageInvalidProgram = "launch: program '{0}' does not exist.";

    public const string MessageCannotFindPdb = "Cannot find or open the PDB file.";
    public const string MessagePdbLoaded = "Symbols loaded.";
    public const string MessagePdfSkipped = "Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.";
}