namespace DotNet.Debugging.Engine.Models;

// The innermost exception wrapped by the one being reported - its recorded trace stands in for the
// wrapper's and the description of the stop names it
public class InnerExceptionInfo {
    public string TypeName { get; }
    public string Message { get; }
    public string? Source { get; }
    public string? StackTrace { get; }
    public int HResult { get; }

    public InnerExceptionInfo(string typeName, string message, string? source, string? stackTrace, int hresult) {
        TypeName = typeName;
        Message = message;
        Source = source;
        StackTrace = stackTrace;
        HResult = hresult;
    }
}
