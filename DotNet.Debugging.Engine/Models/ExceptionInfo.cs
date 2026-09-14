using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

public class ExceptionInfo {
    public string TypeName { get; }
    public string Message { get; }
    public string? StackTrace { get; }
    public ExceptionStopKind Kind { get; }
    // The name of the module the exception is attributed to, shown in "Exception thrown: '...' in <module>"
    public string? ModuleName { get; }
    // The exception wrapped by this one, null without one
    public InnerExceptionInfo? InnerException { get; }

    public ExceptionInfo(string typeName, string message, string? stackTrace, ExceptionStopKind kind, string? moduleName, InnerExceptionInfo? innerException) {
        TypeName = typeName;
        Message = message;
        StackTrace = stackTrace;
        Kind = kind;
        ModuleName = moduleName;
        InnerException = innerException;
    }
}
