using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override NextResponse HandleNextRequest(NextArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            InvokeDebugger(() => session.StepNext(arguments.ThreadId));
            return new NextResponse();
        });
    }
}