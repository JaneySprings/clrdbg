namespace DotNet.Debugging.Engine.Enums;

public enum BreakpointStatus {
    // The debuggee has not been started yet
    Pending,
    // The debuggee is running, but no module containing the location has been loaded yet
    NotProcessed,
    NoSymbols,
    // A module contains an equally named document, but its content differs from the local file
    SourceMismatch,
    NoMatchingFunctions,
    Bound,
    Error,
}
