using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Debugging.Common.Logging;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NewtonConverter = Newtonsoft.Json.JsonConvert;

namespace DotNet.Debugging.Soft.Extensions;

public static class ServerExtensions {
    public static JsonSerializerOptions SerializerOptions { get; } = new JsonSerializerOptions {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    public static ProtocolException GetProtocolException(string message) {
        return new ProtocolException(message, 0, message, url: $"file://{LogConfig.DebugLogFile}");
    }
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
            throw GetProtocolException(ex.Message);
        }
    }

    public static JToken? TryGetValue(this Dictionary<string, JToken> dictionary, string key) {
        if (dictionary.TryGetValue(key, out var value))
            return value;
        return null;
    }
    public static T? ToClass<T>(this JToken? jtoken) where T : class {
        if (jtoken == null)
            return default;

        string json = NewtonConverter.SerializeObject(jtoken);
        if (string.IsNullOrEmpty(json))
            return default;

        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }
    public static T ToValue<T>(this JToken? jtoken) where T : struct {
        if (jtoken == null)
            return default;

        string json = NewtonConverter.SerializeObject(jtoken);
        if (string.IsNullOrEmpty(json))
            return default;

        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
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