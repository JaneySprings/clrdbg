using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments) {
        return Invoke(() => {
            var scopes = InvokeDebugger(() => session.GetScopes(arguments.FrameId));
            return new ScopesResponse(scopes.Select(it => it.ToScope()).ToList());
        });
    }
}