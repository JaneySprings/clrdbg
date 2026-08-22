using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override SetVariableResponse HandleSetVariableRequest(SetVariableArguments arguments) {
        return Invoke(() => {
            var variablesReference = arguments.VariablesReference;
            if (pagingHandles.TryGet(variablesReference, out var page) && page != null)
                variablesReference = page.VariablesReference;

            var variable = InvokeDebugger(() => session.SetVariableAsync(variablesReference, arguments.Name.ToVariableName(), arguments.Value));
            return variable.ToVariable().ToSetVariableResponse();
        });
    }
}