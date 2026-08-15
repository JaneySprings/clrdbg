using DotNet.Debugging.CorApi;
using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override ExceptionInfoResponse HandleExceptionInfoRequest(ExceptionInfoArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var exception = InvokeDebugger(() => session.ExceptionInfo(new ThreadId(arguments.ThreadId)));
            return exception.ToExceptionInfoResponse();
        });
    }
}