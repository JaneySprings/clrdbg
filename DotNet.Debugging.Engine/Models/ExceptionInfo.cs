namespace DotNet.Debugging.Engine.Models;

public class ExceptionInfo {
    public string TypeName { get; }
    public string Message { get; }
    public string? Source { get; }
    public string? StackTrace { get; }
    public int HResult { get; }

    public ExceptionInfo(string typeName, string message, string? source, string? stackTrace, int hresult) {
        TypeName = typeName;
        Message = message;
        Source = source;
        StackTrace = stackTrace;
        HResult = hresult;
    }
}
