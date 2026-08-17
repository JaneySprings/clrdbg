using DotNet.Debugging.Engine;
using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override ExceptionInfoResponse HandleExceptionInfoRequest(ExceptionInfoArguments arguments) {
        return Invoke(() => {
            var exception = InvokeDebugger(() => session.ExceptionInfo(new ThreadId(arguments.ThreadId)));
            return exception.ToExceptionInfoResponse();
        });
    }
}