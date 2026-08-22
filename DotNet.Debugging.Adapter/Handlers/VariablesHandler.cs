using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments) {
        return Invoke(() => {
            var pageOffset = 0;
            var variablesReference = arguments.VariablesReference;
            if (pagingHandles.TryGet(variablesReference, out var page) && page != null) {
                variablesReference = page.VariablesReference;
                pageOffset = page.Offset;
            }

            var variables = InvokeDebugger(() => session.GetVariablesAsync(variablesReference));
            var response = new VariablesResponse(variables
                .Skip(pageOffset)
                .Take(VariablesPageSize)
                .Select(it => it.ToVariable())
                .ToList());

            var remainingCount = variables.Count - pageOffset - VariablesPageSize;
            if (remainingCount > 0) {
                response.Variables.Add(new DebugProtocol.Variable {
                    Name = "[More]",
                    Value = string.Empty,
                    PresentationHint = new DebugProtocol.VariablePresentationHint { Attributes = DebugProtocol.VariablePresentationHint.AttributesValue.ReadOnly },
                    VariablesReference = pagingHandles.Create(new PagedVariablesReference(variablesReference, pageOffset + VariablesPageSize))
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