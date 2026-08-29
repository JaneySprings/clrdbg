using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override SourceResponse HandleSourceRequest(SourceArguments arguments) {
        return Invoke(() => {
            var sourceReference = arguments.Source?.SourceReference ?? arguments.SourceReference;
            var content = sourceLinkResolver.GetSourceContent(sourceReference);
            if (content == null)
                throw new ProtocolException("No source available");

            return new SourceResponse(content);
        });
    }
}