namespace DotNet.Debugging.Adapter;

public static class Resources {
    public const string MsgMissingProcess = "Must specify either processId or processName.";
    public const string MsgNoRunningProcesses = "No process with the specified name is currently running.";
    public const string MsgMultipleProcesses = "Multiple processes were found matching the process name. Attach by process id instead.";
    public const string MsgInvalidProgram = "launch: program '{0}' does not exist.";
    public const string MsgTerminalLaunchFailed = "Unable to launch the program in the terminal: {0}";
    public const string MsgTerminalLaunchTimeout = "Timed out waiting for the terminal host to connect back to the debugger.";

    public const string MsgLicenseBanner =
        "------------------------------------------------------------------------------\n" +
        "You may use the clrdbg .NET debugger with Visual Studio Code, any other editor,\n" +
        "or no editor at all to help you develop and test your applications.\n" +
        "Unlike some debuggers, it does not mind which software you run it from.\n" +
        "------------------------------------------------------------------------------";

    public const string MsgCannotFindPdb = "Cannot find or open the PDB file.";
    public const string MsgPdbLoaded = "Symbols loaded.";
    public const string MsgPdbSearching = "* Searching for '{0}' on the configured symbol servers.";
    public const string MsgPdbSkipped = "Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.";
    public const string MsgPdbSkippedShort = "Skipped loading symbols.";

    public const string MsgBreakpointUnbound = "The breakpoint is not bound yet: no loaded module with symbols contains this location.";
    public const string MsgFunctionBreakpointUnbound = "The breakpoint is not bound yet: no loaded module with symbols has a function matching '{0}'.";
    public const string MsgBreakpointSourceMismatch = "The breakpoint is not bound: the source file differs from the one the module was built from. Set 'requireExactSource' to false in the launch configuration to bind it anyway.";
    public const string MsgBreakpointError = "The breakpoint could not be bound: {0}";
    public const string MsgBreakpointConditionFailed = "The breakpoint condition '{0}' could not be evaluated: {1}";

    public const string MsgExceptionThrown = "Exception thrown: '{0}' in {1}";
    public const string MsgExceptionUnhandled = "An unhandled exception of type '{0}' occurred in {1}";
    public const string MsgExceptionUserUnhandled = "An exception of type '{0}' occurred in {1} but was not handled in user code";
    // Appended to the exception description when the reported exception wraps another one
    public const string MsgExceptionInner = "\nInner exception: {0}: {1}";

    // public const string MsgMissingRuntimeId = $"Missing required property: 'runtimeIdentifier'.";
    public const string MsgMissingAssets = "Missing required property: 'assets'.";
    public const string MsgMissingCoreclrHost =
        "Unable to find the CoreCLR remote debugging host (libremotemscordbihost). " +
        "Please provide a compatible copy from the Microsoft .NET MAUI VS Code extension.";
    public const string MsgMissingCoreclrTarget =
        "Unable to find the CoreCLR remote debugging target (libvsdbgremotecoreclrtarget). " +
        "Please provide a compatible copy from the Microsoft .NET MAUI VS Code extension.";
}