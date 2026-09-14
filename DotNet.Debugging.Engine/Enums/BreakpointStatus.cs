namespace DotNet.Debugging.Engine.Enums;

public enum BreakpointStatus {
    // No loaded module with symbols contains the location (a function breakpoint: matches the function) yet
    Unbound,
    // A module contains an equally named document, but its content differs from the local file
    SourceMismatch,
    Bound,
    Error,
}
