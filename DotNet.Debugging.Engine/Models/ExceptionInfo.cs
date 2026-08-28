using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

public class ExceptionInfo {
    public string TypeName { get; }
    public string Message { get; }
    public string? Source { get; }
    public string? StackTrace { get; }
    public int HResult { get; }
    public ExceptionStopKind Kind { get; }
    // The name of the module the exception is attributed to, shown in "Exception thrown: '...' in <module>"
    public string? ModuleName { get; }
    // The 'InnerException' chain, the direct inner first - empty when there is none
    public IReadOnlyList<InnerExceptionInfo> InnerExceptionChain { get; }

    public ExceptionInfo(string typeName, string message, string? source, string? stackTrace, int hresult, ExceptionStopKind kind, string? moduleName, IReadOnlyList<InnerExceptionInfo> innerExceptionChain) {
        TypeName = typeName;
        Message = message;
        Source = source;
        StackTrace = stackTrace;
        HResult = hresult;
        Kind = kind;
        ModuleName = moduleName;
        InnerExceptionChain = innerExceptionChain;
    }
}
