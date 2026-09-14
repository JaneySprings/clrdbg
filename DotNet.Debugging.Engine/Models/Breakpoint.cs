using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Extensions;

namespace DotNet.Debugging.Engine.Models;

public class Breakpoint {
    public int Id { get; }
    public string? FilePath { get; }
    public string? FunctionName { get; }
    public string? Condition { get; }
    public string? HitCondition { get; }
    public string? LogMessage { get; }
    public int RequestedLine { get; }
    public int? RequestedColumn { get; }
    public BreakpointStatus Status { get; internal set; }
    // Details of a 'BreakpointStatus.Error'
    public string? Error { get; internal set; }
    public int HitCount { get; internal set; }
    // Where the breakpoint is bound (a function breakpoint: its first binding), with the document's Source Link
    public SourceLocation? Location { get; internal set; }

    public bool Verified => Status == BreakpointStatus.Bound;
    public bool IsFunctionBreakpoint => FunctionName != null;
    // The bound line, the requested one until then
    public int Line => Location?.Line ?? RequestedLine;

    // One for a source breakpoint, one per matching method for a function breakpoint
    internal List<BreakpointBinding> Bindings { get; }
    // A source breakpoint bound to a document matched by path or checksum rather than by file name alone
    internal bool IsExactMatch { get; set; }

    public Breakpoint(int id, string filePath, BreakpointRequest request) {
        Id = id;
        FilePath = filePath;
        RequestedLine = request.Line;
        RequestedColumn = request.Column;
        Condition = request.Condition.NullIfWhiteSpace();
        HitCondition = request.HitCondition.NullIfWhiteSpace();
        LogMessage = request.LogMessage.NullIfWhiteSpace();
        Bindings = new List<BreakpointBinding>();
    }
    public Breakpoint(int id, FunctionBreakpointRequest request) {
        Id = id;
        FunctionName = request.Name;
        Condition = request.Condition.NullIfWhiteSpace();
        HitCondition = request.HitCondition.NullIfWhiteSpace();
        Bindings = new List<BreakpointBinding>();
    }

    internal void SetStatus(BreakpointStatus status, string? error = null) {
        Status = status;
        Error = error;
    }
}
