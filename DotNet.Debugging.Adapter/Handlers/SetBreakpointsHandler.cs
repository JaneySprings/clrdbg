using DotNet.Debugging.Adapter.Extensions;
using DotNet.Debugging.Engine.Models;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments) {
        return Invoke(() => {
            var sourcePath = arguments.Source?.Path;
            if (string.IsNullOrEmpty(sourcePath))
                throw new ProtocolException("No source available for the breakpoint");

            var requests = (arguments.Breakpoints ?? new List<SourceBreakpoint>()).Select(it => new BreakpointRequest(it.Line) {
                Column = it.Column,
                Condition = it.Condition,
                HitCondition = it.HitCondition,
                LogMessage = it.LogMessage,
            }).ToList();

            var breakpoints = InvokeDebugger(() => session.SetBreakpoints(sourcePath, requests));
            return new SetBreakpointsResponse(breakpoints.Select(it => it.ToBreakpoint(sourceLinkResolver, session)).ToList());
        });
    }

    private void ReportFailedCondition(FailedCondition failedCondition) {
        var breakpoint = failedCondition.Breakpoint;
        var filePath = breakpoint.Location != null ? session.MapDocumentPath(breakpoint.Location.FilePath) : breakpoint.FilePath;
        OnDebugDataReceived($"{failedCondition.ToDisplayMessage()} ({filePath}:{breakpoint.Line})");
    }
}