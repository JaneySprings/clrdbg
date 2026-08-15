using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override GotoTargetsResponse HandleGotoTargetsRequest(GotoTargetsArguments arguments) {
        var targetId = gotoHandles.Create(new SourceLocation(arguments.Source.Path, arguments.Line));
        return arguments.ToJumpToCursorTarget(targetId);
    }
    protected override GotoResponse HandleGotoRequest(GotoArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var target = gotoHandles.Get(arguments.TargetId, null);
            if (target == null)
                throw new ProtocolException("GotoTarget not found");

            InvokeDebugger(() => session.SetNextStatement(arguments.ThreadId, target.FileName, target.Line));
            Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Goto) {
                ThreadId = arguments.ThreadId,
                AllThreadsStopped = true,
            });
            return new GotoResponse();
        });
    }
}

public class SourceLocation {
    public string FileName { get; }
    public int Line { get; }

    public SourceLocation(string fileName, int line) {
        FileName = fileName;
        Line = line;
    }
}