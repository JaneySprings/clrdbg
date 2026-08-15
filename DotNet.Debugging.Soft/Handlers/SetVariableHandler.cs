using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override SetVariableResponse HandleSetVariableRequest(SetVariableArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var variablesReference = arguments.VariablesReference;
            if (pagingHandles.TryGet(variablesReference, out var page) && page != null)
                variablesReference = page.VariablesReference;

            var variable = InvokeDebugger(() => session.SetVariableValue(variablesReference, arguments.Name.ToVariableName(), arguments.Value));
            return variable.ToVariable().ToSetVariableResponse();
        });
    }
}