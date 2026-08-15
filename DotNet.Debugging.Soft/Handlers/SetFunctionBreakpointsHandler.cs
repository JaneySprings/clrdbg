using DotNet.Debugging.CorApi;
using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override SetFunctionBreakpointsResponse HandleSetFunctionBreakpointsRequest(SetFunctionBreakpointsArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var breakpointsInfos = arguments.Breakpoints ?? new List<FunctionBreakpoint>();
            var requests = breakpointsInfos.Select(it => new FunctionBreakpointRequest(it.Name, it.Condition, it.HitCondition)).ToArray();
            var breakpoints = InvokeDebugger(() => session.SetFunctionBreakpoints(requests));
            return new SetFunctionBreakpointsResponse(breakpoints.Select(it => it.ToBreakpoint()).ToList());
        });
    }
}