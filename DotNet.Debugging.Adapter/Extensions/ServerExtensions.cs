using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.Engine;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using CorDebugModels = DotNet.Debugging.Engine.PresentationHintModels;
using CorDebugResponse = DotNet.Debugging.Engine.Models.Response;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter.Extensions;

public static class ServerExtensions {
    public static void TrySendEvent(this DebugProtocolClient protocol, DebugEvent ev) {
        try {
            protocol.SendEvent(ev);
        }
        catch (Exception ex) {
            CurrentSessionLogger.Error($"[Handled] {ex.ToString()}");
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

    public static DebugProtocol.ExceptionInfoResponse ToExceptionInfoResponse(this CorDebugResponse.ExceptionInfo exception) {
        return new DebugProtocol.ExceptionInfoResponse(exception.ExceptionId, exception.BreakMode.ToExceptionBreakMode()) {
            Description = exception.Description,
            Code = exception.Code,
            Details = exception.Details.ToExceptionDetails(),
        };
    }
    private static DebugProtocol.ExceptionBreakMode ToExceptionBreakMode(this CorDebugResponse.ExceptionBreakMode breakMode) {
        return breakMode switch {
            CorDebugResponse.ExceptionBreakMode.Always => DebugProtocol.ExceptionBreakMode.Always,
            CorDebugResponse.ExceptionBreakMode.Never => DebugProtocol.ExceptionBreakMode.Never,
            CorDebugResponse.ExceptionBreakMode.Unhandled => DebugProtocol.ExceptionBreakMode.Unhandled,
            CorDebugResponse.ExceptionBreakMode.UserUnhandled => DebugProtocol.ExceptionBreakMode.UserUnhandled,
            _ => DebugProtocol.ExceptionBreakMode.Unknown
        };
    }
    private static DebugProtocol.ExceptionDetails? ToExceptionDetails(this CorDebugResponse.ExceptionInfo.ExceptionDetails? details) {
        if (details == null)
            return null;

        return new DebugProtocol.ExceptionDetails {
            Message = details.Message,
            TypeName = details.TypeName,
            FullTypeName = details.FullTypeName,
            EvaluateName = details.EvaluateName,
            StackTrace = details.StackTrace,
            InnerException = details.InnerException?.Select(it => it.ToExceptionDetails()).ToList(),
            FormattedDescription = details.FormattedDescription,
            HResult = details.HResult,
            Source = details.Source,
        };
    }

    public static DebugProtocol.Breakpoint ToBreakpoint(this BreakpointManager.BreakpointInfo breakpoint) {
        return new DebugProtocol.Breakpoint() {
            Id = breakpoint.Id,
            Verified = breakpoint.Verified,
            Message = breakpoint.Message,
            Line = breakpoint.IsFunctionBreakpoint ? null : breakpoint.Line,
            Column = breakpoint is { IsFunctionBreakpoint: false, Verified: true } ? breakpoint.Column : null,
            EndLine = breakpoint is { IsFunctionBreakpoint: false, Verified: true } ? breakpoint.EndLine : null,
            EndColumn = breakpoint is { IsFunctionBreakpoint: false, Verified: true } ? breakpoint.EndColumn : null,
            Source = breakpoint is not { IsFunctionBreakpoint: false, Verified: true } ? null : new DebugProtocol.Source {
                Path = breakpoint.FilePath,
                Name = Path.GetFileName(breakpoint.FilePath),
            }
        };
    }
    public static DebugProtocol.StackFrame ToStackFrame(this CorDebugResponse.StackFrameInfo frame) {
        DebugProtocol.Source? source = null;
        if (!string.IsNullOrEmpty(frame.Source)) {
            source = new DebugProtocol.Source() {
                Name = Path.GetFileName(frame.Source),
                Path = frame.Source,
            };
        }
        return new DebugProtocol.StackFrame() {
            Id = frame.Id,
            Source = source,
            Name = frame.Name,
            Line = frame.Line,
            Column = frame.Column,
            EndLine = frame.EndLine,
            EndColumn = frame.EndColumn,
            PresentationHint = DebugProtocol.StackFrame.PresentationHintValue.Normal
        };
    }
    public static DebugProtocol.Scope ToScope(this CorDebugResponse.ScopeInfo scope) {
        return new DebugProtocol.Scope() {
            Name = scope.Name,
            PresentationHint = scope.Name == "Locals" ? DebugProtocol.Scope.PresentationHintValue.Locals : DebugProtocol.Scope.PresentationHintValue.Unknown,
            VariablesReference = scope.VariablesReference,
            Expensive = scope.Expensive
        };
    }
    public static DebugProtocol.Variable ToVariable(this CorDebugResponse.VariableInfo variable) {
        return new DebugProtocol.Variable {
            Name = variable.Name.ToDisplayName(variable.Type),
            Type = variable.Type,
            Value = variable.Value,
            EvaluateName = variable.Name,
            PresentationHint = variable.PresentationHint?.ToVariablePresentationHint(),
            VariablesReference = variable.VariablesReference
        };
    }

    public static DebugProtocol.VariablePresentationHint ToVariablePresentationHint(this CorDebugModels.VariablePresentationHint hint) {
        return new DebugProtocol.VariablePresentationHint {
            Kind = hint.Kind?.ToKindValue(),
            Attributes = hint.Attributes?.ToAttributesValue(),
            Visibility = null
        };
    }
    private static DebugProtocol.VariablePresentationHint.KindValue ToKindValue(this CorDebugModels.PresentationHintKind kind) {
        return kind switch {
            CorDebugModels.PresentationHintKind.Property => DebugProtocol.VariablePresentationHint.KindValue.Property,
            CorDebugModels.PresentationHintKind.Method => DebugProtocol.VariablePresentationHint.KindValue.Method,
            CorDebugModels.PresentationHintKind.Event => DebugProtocol.VariablePresentationHint.KindValue.Event,
            CorDebugModels.PresentationHintKind.Class => DebugProtocol.VariablePresentationHint.KindValue.Class,
            CorDebugModels.PresentationHintKind.Data => DebugProtocol.VariablePresentationHint.KindValue.Data,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
    private static DebugProtocol.VariablePresentationHint.AttributesValue ToAttributesValue(this CorDebugModels.AttributesValue attributes) {
        return attributes switch {
            CorDebugModels.AttributesValue.FailedEvaluation => DebugProtocol.VariablePresentationHint.AttributesValue.FailedEvaluation,
            _ => throw new ArgumentOutOfRangeException(nameof(attributes), attributes, null)
        };
    }

    public static StoppedEvent.ReasonValue ToStoppedReason(this string reason) {
        return reason.ToLowerInvariant() switch {
            "step" => StoppedEvent.ReasonValue.Step,
            "breakpoint" => StoppedEvent.ReasonValue.Breakpoint,
            "exception" => StoppedEvent.ReasonValue.Exception,
            "pause" => StoppedEvent.ReasonValue.Pause,
            "entry" => StoppedEvent.ReasonValue.Entry,
            "goto" => StoppedEvent.ReasonValue.Goto,
            "function breakpoint" => StoppedEvent.ReasonValue.FunctionBreakpoint,
            "data breakpoint" => StoppedEvent.ReasonValue.DataBreakpoint,
            "instruction breakpoint" => StoppedEvent.ReasonValue.InstructionBreakpoint,
            _ => StoppedEvent.ReasonValue.Unknown
        };
    }
}