namespace DotNet.Debugging.Engine.Enums;

public enum ExceptionStopKind {
    FirstChance,
    // Thrown in (or passed through) user code and about to be caught in non-user code
    UserUnhandled,
    Unhandled,
}
