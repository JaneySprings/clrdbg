using DotNet.Debugging.Adapter.Extensions;
using DotNet.Debugging.Engine.Models;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override GotoTargetsResponse HandleGotoTargetsRequest(GotoTargetsArguments arguments) {
        var sourcePath = sourceFileMapper.ToCompilerPath(arguments.Source.Path);
        var targetId = gotoHandles.Create(new SourceLocation(sourcePath, arguments.Line, arguments.Column ?? 0, arguments.Line, 0));
        return arguments.ToJumpToCursorTarget(targetId);
    }
    protected override GotoResponse HandleGotoRequest(GotoArguments arguments) {
        return Invoke(() => {
            var target = gotoHandles.Get(arguments.TargetId);
            if (target == null)
                throw new ProtocolException("GotoTarget not found");

            InvokeDebugger(() => session.SetNextStatement(arguments.ThreadId, target.FilePath, target.Line));
            Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Goto) {
                ThreadId = arguments.ThreadId,
                AllThreadsStopped = true,
            });
            return new GotoResponse();
        });
    }
}