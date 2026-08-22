using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments) {
        return Invoke(() => {
            var localsReference = InvokeDebugger(() => session.GetLocalsReference(arguments.FrameId));
            var scopes = new List<DebugProtocol.Scope>();
            if (localsReference != 0) {
                scopes.Add(new DebugProtocol.Scope() {
                    Name = "Locals",
                    PresentationHint = DebugProtocol.Scope.PresentationHintValue.Locals,
                    VariablesReference = localsReference,
                    Expensive = false
                });
            }
            return new ScopesResponse(scopes);
        });
    }
}