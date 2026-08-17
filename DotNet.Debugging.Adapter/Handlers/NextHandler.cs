using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override NextResponse HandleNextRequest(NextArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => session.StepNext(arguments.ThreadId));
            return new NextResponse();
        });
    }
}