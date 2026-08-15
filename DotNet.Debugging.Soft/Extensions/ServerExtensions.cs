using DotNet.Debugging.Common.Logging;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft.Extensions;

public static class ServerExtensions {
    public static string? TrimExpression(this DebugProtocol.EvaluateArguments args) {
        return args.Expression?.TrimEnd(';');
    }
    public static void TrySendEvent(this DebugProtocolClient protocol, DebugEvent ev) {
        try {
            protocol.SendEvent(ev);
        }
        catch (Exception ex) {
            CurrentSessionLogger.Error($"[Handled] {ex.ToString()}");
        }
    }

    public static T DoSafe<T>(Func<T> handler, Action? finalizer = null) {
        try {
            return handler.Invoke();
        }
        catch (Exception ex) {
            finalizer?.Invoke();
            if (ex is ProtocolException)
                throw;
            CurrentSessionLogger.Error($"[Handled] {ex.ToString()}");
            throw Session.GetProtocolException(ex.Message);
        }
    }

    public static DebugProtocol.SetVariableResponse ToSetVariableResponse(this DebugProtocol.Variable variable) {
        return new DebugProtocol.SetVariableResponse {
            Value = variable.Value,
            Type = variable.Type,
            VariablesReference = variable.VariablesReference,
            NamedVariables = variable.NamedVariables,
            IndexedVariables = variable.IndexedVariables,
        };
    }
    public static DebugProtocol.GotoTargetsResponse ToJumpToCursorTarget(this DebugProtocol.GotoTargetsArguments args, int id) {
        return new DebugProtocol.GotoTargetsResponse {
            Targets = new List<DebugProtocol.GotoTarget>() {
            new DebugProtocol.GotoTarget {
                Id = id,
                Label = "Jump to cursor",
                Line = args.Line,
                Column = args.Column,
                EndLine = 0,
                EndColumn = 0
            }
        }
        };
    }
}