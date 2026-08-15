using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var frames = InvokeDebugger(() => session.GetStackTrace(arguments.ThreadId, arguments.StartFrame ?? 0, arguments.Levels));
            return new StackTraceResponse(frames.Select(it => it.ToStackFrame()).ToList());
        });
    }
}