using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

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
        foreach (var option in arguments.FilterOptions)
            GetExceptionFilterOptions(option.FilterId)?.Enable(option.Condition);

        // One breakpoint per requested filter, in request order: filters first, then filterOptions. Unknown filters stay unverified
        var breakpoints = (arguments.Filters ?? new List<string>())
            .Concat((arguments.FilterOptions).Select(it => it.FilterId))
            .Select(filterId => new DebugProtocol.Breakpoint { Verified = GetExceptionFilterOptions(filterId) != null })
            .ToList();
        return new SetExceptionBreakpointsResponse() { Breakpoints = breakpoints };
    }
}