using DotNet.Debugging.Adapter.Extensions;
using DotNet.Debugging.Engine.Models;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override SetFunctionBreakpointsResponse HandleSetFunctionBreakpointsRequest(SetFunctionBreakpointsArguments arguments) {
        return Invoke(() => {
            var requests = (arguments.Breakpoints ?? new List<FunctionBreakpoint>()).Select(it => new FunctionBreakpointRequest(it.Name) {
                Condition = it.Condition,
                HitCondition = it.HitCondition,
            }).ToList();

            var breakpoints = InvokeDebugger(() => session.SetFunctionBreakpoints(requests));
            return new SetFunctionBreakpointsResponse(breakpoints.Select(it => it.ToBreakpoint(sourceLinkResolver, sourceFileMapper)).ToList());
        });
    }
}