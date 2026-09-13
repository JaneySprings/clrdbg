using DotNet.Debugging.Adapter.Extensions;
using DotNet.Debugging.Engine.Models;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Breakpoint = DotNet.Debugging.Engine.Models.Breakpoint;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments) {
        return Invoke(() => {
            var sourcePath = arguments.Source?.Path;
            if (string.IsNullOrEmpty(sourcePath))
                throw new ProtocolException("No source available for the breakpoint");
            sourcePath = sourceFileMapper.ToCompilerPath(sourcePath);

            var requests = (arguments.Breakpoints ?? new List<SourceBreakpoint>()).Select(it => new BreakpointRequest(it.Line) {
                Column = it.Column,
                Condition = it.Condition,
                HitCondition = it.HitCondition,
                LogMessage = it.LogMessage,
            }).ToList();

            var breakpoints = InvokeDebugger(() => session.SetBreakpoints(sourcePath, requests));
            return new SetBreakpointsResponse(breakpoints.Select(it => it.ToBreakpoint(sourceLinkResolver, sourceFileMapper)).ToList());
        });
    }

    private void ReportFailedCondition(FailedCondition failedCondition) {
        var message = failedCondition.ToDisplayMessage();
        var breakpoint = failedCondition.Breakpoint.ToBreakpoint(sourceLinkResolver, sourceFileMapper);
        breakpoint.Message = message;
        Protocol.SendEvent(new BreakpointEvent(BreakpointEvent.ReasonValue.Changed, breakpoint));
        ReportBreakpointWarning(failedCondition.Breakpoint, message);
    }
    private void ReportBreakpointWarning(Breakpoint breakpoint, string message) {
        var filePath = sourceFileMapper.ToLocalPath(breakpoint.Location?.FilePath ?? breakpoint.FilePath ?? string.Empty);
        OnDebugDataReceived(string.Format(Resources.MsgBreakpointWarning, message, filePath, breakpoint.Line));
    }
}