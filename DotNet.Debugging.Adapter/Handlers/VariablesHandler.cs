using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    private const int VariablesPageSize = 25;

    protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments) {
        return Invoke(() => {
            var pageOffset = 0;
            var variablesReference = arguments.VariablesReference;
            if (pagingHandles.TryGet(variablesReference, out var page) && page != null) {
                variablesReference = page.VariablesReference;
                pageOffset = page.Offset;
            }

            // Only the requested page is read from the debuggee, the rest of the listing stays unevaluated
            var variables = InvokeDebugger(() => session.GetVariablesAsync(variablesReference, pageOffset, VariablesPageSize));
            var response = new VariablesResponse(variables.Variables.Select(it => it.ToVariable()).ToList());

            var nextOffset = pageOffset + VariablesPageSize;
            if (variables.TotalCount > nextOffset) {
                response.Variables.Add(new DebugProtocol.Variable {
                    Name = "[More]",
                    Value = string.Empty,
                    PresentationHint = new DebugProtocol.VariablePresentationHint { Attributes = DebugProtocol.VariablePresentationHint.AttributesValue.ReadOnly },
                    VariablesReference = pagingHandles.Create(new PagedVariablesReference(variablesReference, nextOffset))
                });
            }

            return response;
        });
    }
}

public class PagedVariablesReference {
    public int VariablesReference { get; }
    public int Offset { get; }

    public PagedVariablesReference(int variablesReference, int offset) {
        VariablesReference = variablesReference;
        Offset = offset;
    }
}