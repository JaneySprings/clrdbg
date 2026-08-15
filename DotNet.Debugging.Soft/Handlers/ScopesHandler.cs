using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var scopes = InvokeDebugger(() => session.GetScopes(arguments.FrameId));
            return new ScopesResponse(scopes.Select(it => it.ToScope()).ToList());
        });
    }
}