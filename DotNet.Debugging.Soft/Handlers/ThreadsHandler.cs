using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override ThreadsResponse HandleThreadsRequest(ThreadsArguments arguments) {
        return Invoke(() => {
            var threads = InvokeDebugger(() => session.GetThreads());
            return new ThreadsResponse(threads.Select(it => new DebugProtocol.Thread(it.id, it.name.ToThreadName(it.id))).ToList());
        });
    }
}