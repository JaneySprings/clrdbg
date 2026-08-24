using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override CompletionsResponse HandleCompletionsRequest(CompletionsArguments arguments) {
        // skip
        return new CompletionsResponse(new List<CompletionItem>());
    }
}
