using DotNet.Debugging.CorApi;
using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

public class Breakpoint {
    public int Id { get; }
    public string? FilePath { get; }
    public string? FunctionName { get; }
    public string? Condition { get; }
    public string? HitCondition { get; }
    public string? LogMessage { get; }
    public int Line { get; internal set; }
    public int? Column { get; internal set; }
    public int EndLine { get; internal set; }
    public int? EndColumn { get; internal set; }
    public BreakpointStatus Status { get; internal set; }
    // Details of a 'BreakpointStatus.Error'
    public string? Error { get; internal set; }
    public int HitCount { get; internal set; }
    // The location the breakpoint is bound to, with the document's checksum and Source Link
    public SourceLocation? Location { get; internal set; }

    public bool Verified => Status == BreakpointStatus.Bound;
    public bool IsFunctionBreakpoint => FunctionName != null;

    internal ICorDebugFunctionBreakpoint? CorBreakpoint { get; set; }
    internal ResolvedBreakpoint? ResolvedLocation { get; set; }
    internal List<FunctionBreakpointBinding> FunctionBindings { get; }

    public Breakpoint(int id, string filePath, BreakpointRequest request) {
        Id = id;
        FilePath = filePath;
        Line = request.Line;
        Column = request.Column;
        Condition = NormalizeExpression(request.Condition);
        HitCondition = NormalizeExpression(request.HitCondition);
        LogMessage = NormalizeExpression(request.LogMessage);
        FunctionBindings = new List<FunctionBreakpointBinding>();
    }
    public Breakpoint(int id, FunctionBreakpointRequest request) {
        Id = id;
        FunctionName = request.Name;
        Condition = NormalizeExpression(request.Condition);
        HitCondition = NormalizeExpression(request.HitCondition);
        FunctionBindings = new List<FunctionBreakpointBinding>();
    }

    internal void SetStatus(BreakpointStatus status, string? error = null) {
        Status = status;
        Error = error;
    }

    private static string? NormalizeExpression(string? expression) {
        return string.IsNullOrWhiteSpace(expression) ? null : expression;
    }
}
