namespace DotNet.Debugging.Engine.Models;

// The exception wrapped by the one being reported
public class InnerExceptionInfo {
    public string TypeName { get; }
    public string Message { get; }
    public string? StackTrace { get; }

    public InnerExceptionInfo(string typeName, string message, string? stackTrace) {
        TypeName = typeName;
        Message = message;
        StackTrace = stackTrace;
    }
}
