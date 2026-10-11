namespace DotNet.Debugging.Engine.Enums;

public enum BreakpointStatus {
    // No loaded module with symbols contains the location (a function breakpoint: matches the function) yet
    Unbound,
    // A module contains the document, but its content differs from the local file (edited since the build, or
    // an equally named file of another project)
    SourceMismatch,
    // A module contains the document, but no code is associated with the line
    NoCode,
    Bound,
    Error,
}
