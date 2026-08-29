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
}