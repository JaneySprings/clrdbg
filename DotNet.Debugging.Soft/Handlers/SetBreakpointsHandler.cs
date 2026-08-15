using DotNet.Debugging.CorApi;
using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var sourcePath = arguments.Source?.Path;
            if (string.IsNullOrEmpty(sourcePath))
                throw new ProtocolException("No source available for the breakpoint");

            var breakpointsInfos = arguments.Breakpoints ?? new List<SourceBreakpoint>();
            var requests = breakpointsInfos
                .Select(it => new BreakpointRequest(it.Line, it.Condition, it.HitCondition, it.Column))
                .ToArray();

            var breakpoints = InvokeDebugger(() => {
                var result = session.SetBreakpoints(sourcePath, requests);
                // Rebind logpoint messages to the new breakpoint identifiers
                if (logpointIdsByFile.TryGetValue(sourcePath, out var staleIds)) {
                    foreach (var staleId in staleIds)
                        logpointMessages.Remove(staleId);
                }
                var fileLogpointIds = new List<int>();
                for (var i = 0; i < result.Count && i < breakpointsInfos.Count; i++) {
                    if (string.IsNullOrEmpty(breakpointsInfos[i].LogMessage))
                        continue;
                    logpointMessages[result[i].Id] = breakpointsInfos[i].LogMessage;
                    fileLogpointIds.Add(result[i].Id);
                }
                logpointIdsByFile[sourcePath] = fileLogpointIds;
                return result;
            });

            return new SetBreakpointsResponse(breakpoints.Select(it => it.ToBreakpoint()).ToList());
        });
    }
}