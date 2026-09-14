using DotNet.Debugging.Engine.Enums;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override NextResponse HandleNextRequest(NextArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => session.StepAsync(arguments.ThreadId, StepKind.Over));
            return new NextResponse();
        });
    }
}