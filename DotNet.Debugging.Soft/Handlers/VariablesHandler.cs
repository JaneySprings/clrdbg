using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var pageOffset = 0;
            var variablesReference = arguments.VariablesReference;
            if (pagingHandles.TryGet(variablesReference, out var page) && page != null) {
                variablesReference = page.VariablesReference;
                pageOffset = page.Offset;
            }

            var variables = InvokeDebugger(() => session.GetVariables(variablesReference));
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
                    VariablesReference = pagingHandles.Create(new PagedVariablesReference(variablesReference, pageOffset + VariablesPageSize))
                });
            }

            return response;
        });
    }
}