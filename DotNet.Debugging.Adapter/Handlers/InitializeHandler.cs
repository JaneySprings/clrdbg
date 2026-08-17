using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments) {
        return new InitializeResponse() {
            SupportsConfigurationDoneRequest = true,
            SupportsTerminateRequest = true,
            SupportsEvaluateForHovers = true,
            SupportsExceptionInfoRequest = true,
            SupportsConditionalBreakpoints = true,
            SupportsHitConditionalBreakpoints = true,
            SupportsFunctionBreakpoints = true,
            SupportsLogPoints = true,
            SupportsExceptionFilterOptions = true,
            SupportsCompletionsRequest = true,
            SupportsSetVariable = true,
            SupportsGotoTargetsRequest = true,
            ExceptionBreakpointFilters = new List<ExceptionBreakpointsFilter> {
                ExceptionsFilter.AllExceptions,
                ExceptionsFilter.UserUnhandledExceptions
            }
        };
    }
}