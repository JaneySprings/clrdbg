using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments) {
        return Invoke(() => {
            var frames = InvokeDebugger(() => session.GetStackFrames(arguments.ThreadId));
            var stackFrames = frames
                .Skip(arguments.StartFrame ?? 0)
                .Take(arguments.Levels ?? int.MaxValue)
                .Select(it => it.ToStackFrame(moduleHandles.FindHandle(it.ModulePath), sourceLinkResolver))
                .ToList();

            return new StackTraceResponse(stackFrames) { TotalFrames = frames.Count };
        });
    }
}