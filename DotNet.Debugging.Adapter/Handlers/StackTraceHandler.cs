using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments) {
        return Invoke(() => {
            var stackTrace = InvokeDebugger(() => session.GetStackTrace(arguments.ThreadId, arguments.StartFrame ?? 0, arguments.Levels));
            return new StackTraceResponse(stackTrace.Frames.Select(it => it.ToStackFrame(moduleHandles.FindHandle(it.ModulePath))).ToList()) {
                TotalFrames = stackTrace.TotalFrames
            };
        });
    }
}