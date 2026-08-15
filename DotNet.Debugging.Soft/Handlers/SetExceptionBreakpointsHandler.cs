using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override SetExceptionBreakpointsResponse HandleSetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments) {
        allExceptionsFilter.Reset();
        userUnhandledExceptionsFilter.Reset();

        if (arguments.Filters != null) {
            if (arguments.Filters.Contains(ExceptionsFilter.AllExceptions.Filter))
                allExceptionsFilter.Enable();
            if (arguments.Filters.Contains(ExceptionsFilter.UserUnhandledExceptions.Filter))
                userUnhandledExceptionsFilter.Enable();
        }
        if (arguments.FilterOptions == null || arguments.FilterOptions.Count == 0)
            return new SetExceptionBreakpointsResponse();

        foreach (var option in arguments.FilterOptions) {
            var filter = GetExceptionFilterOptions(option.FilterId);
            filter?.Enable(option.Condition);
        }
        return new SetExceptionBreakpointsResponse();
    }
}