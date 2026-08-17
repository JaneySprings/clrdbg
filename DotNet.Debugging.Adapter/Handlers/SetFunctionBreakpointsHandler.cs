using DotNet.Debugging.Engine;
using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override SetFunctionBreakpointsResponse HandleSetFunctionBreakpointsRequest(SetFunctionBreakpointsArguments arguments) {
        return Invoke(() => {
            var breakpointsInfos = arguments.Breakpoints ?? new List<FunctionBreakpoint>();
            var requests = breakpointsInfos.Select(it => new FunctionBreakpointRequest(it.Name, it.Condition, it.HitCondition)).ToArray();
            var breakpoints = InvokeDebugger(() => session.SetFunctionBreakpoints(requests));
            return new SetFunctionBreakpointsResponse(breakpoints.Select(it => it.ToBreakpoint()).ToList());
        });
    }
}