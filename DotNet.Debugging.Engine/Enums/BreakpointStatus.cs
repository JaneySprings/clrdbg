namespace DotNet.Debugging.Engine.Enums;

public enum BreakpointStatus {
    // The debuggee has not been started yet
    Pending,
    // The debuggee is running, but no module containing the location has been loaded yet
    NotProcessed,
    NoSymbols,
    NoMatchingFunctions,
    Bound,
    Error,
}
