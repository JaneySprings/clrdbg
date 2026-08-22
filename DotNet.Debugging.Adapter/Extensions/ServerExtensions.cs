using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Models;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Breakpoint = DotNet.Debugging.Engine.Models.Breakpoint;

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

    public static DebugProtocol.ExceptionInfoResponse ToExceptionInfoResponse(this ExceptionInfo exception) {
        var shortTypeName = exception.TypeName.Substring(exception.TypeName.LastIndexOf('.') + 1);
        return new DebugProtocol.ExceptionInfoResponse($"CLR/{exception.TypeName}", DebugProtocol.ExceptionBreakMode.Always) {
            Description = $"Exception thrown: '{exception.TypeName}' in {exception.Source}.dll: '{exception.Message}'",
            Code = 0,
            Details = new DebugProtocol.ExceptionDetails {
                Message = exception.Message,
                TypeName = shortTypeName,
                FullTypeName = exception.TypeName,
                EvaluateName = "$exception",
                StackTrace = exception.StackTrace,
                InnerException = new List<DebugProtocol.ExceptionDetails>(),
                FormattedDescription = $"**{exception.TypeName}:** '{exception.Message}'",
                HResult = exception.HResult,
                Source = exception.Source,
            },
        };
    }
    public static DebugProtocol.Source ToSource(this SourceLocation location, SourceLinkResolver? sourceLinkResolver) {
        // The library pre-populates 'sources' and 'checksums' with empty lists, which vsdbg never sends
        var source = new DebugProtocol.Source {
            Name = Path.GetFileName(location.FilePath),
            Path = location.FilePath,
            Sources = null,
            Checksums = null,
        };
        if (location.Checksum != null && Enum.TryParse<DebugProtocol.ChecksumAlgorithm>(location.Checksum.Algorithm, out var algorithm))
            source.Checksums = new List<DebugProtocol.Checksum> { new DebugProtocol.Checksum(algorithm, location.Checksum.Value) };
        if (location.SourceLink != null) {
            source.VsSourceLinkInfo = new DebugProtocol.VSSourceLinkInfo { Url = location.SourceLink, RelativeFilePath = location.FilePath };
            // A document that does not exist locally is served through the 'source' request, which downloads it
            if (sourceLinkResolver != null && !File.Exists(location.FilePath))
                source.SourceReference = sourceLinkResolver.GetSourceReference(location.SourceLink);
        }
        return source;
    }
    public static DebugProtocol.Module ToModule(this ModuleInfo module, int moduleId, bool justMyCode) {
        var symbolStatus = Resources.MsgCannotFindPdb;
        if (module.HasSymbols)
            symbolStatus = Resources.MsgPdbLoaded;
        else if (!module.IsUserCode && justMyCode)
            symbolStatus = Resources.MsgPdbSkippedShort;

        return new DebugProtocol.Module {
            Id = moduleId,
            Name = module.Name,
            Path = module.Path,
            IsOptimized = !module.IsUserCode,
            IsUserCode = module.IsUserCode,
            Version = module.Version.ToDisplayVersion(),
            SymbolStatus = symbolStatus,
            SymbolFilePath = module.HasSymbols ? module.SymbolFilePath : null,
        };
    }
    public static DebugProtocol.Breakpoint ToBreakpoint(this Breakpoint breakpoint, SourceLinkResolver? sourceLinkResolver) {
        var isBoundSourceBreakpoint = !breakpoint.IsFunctionBreakpoint && breakpoint.Verified;
        return new DebugProtocol.Breakpoint() {
            Id = breakpoint.Id,
            Verified = breakpoint.Verified,
            Message = breakpoint.ToStatusMessage(),
            Line = breakpoint.IsFunctionBreakpoint ? null : breakpoint.Line,
            Column = isBoundSourceBreakpoint ? breakpoint.Column : null,
            EndLine = isBoundSourceBreakpoint ? breakpoint.EndLine : null,
            EndColumn = isBoundSourceBreakpoint ? breakpoint.EndColumn : null,
            Source = isBoundSourceBreakpoint ? breakpoint.Location?.ToSource(sourceLinkResolver) : null,
        };
    }
    public static DebugProtocol.StackFrame ToStackFrame(this StackFrameInfo frame, int? moduleId, SourceLinkResolver? sourceLinkResolver) {
        return new DebugProtocol.StackFrame() {
            Id = frame.Id,
            Source = frame.Location?.ToSource(sourceLinkResolver),
            Name = frame.ToDisplayName(),
            Line = frame.Location?.Line ?? 0,
            Column = frame.Location?.Column ?? 0,
            EndLine = frame.Location?.EndLine ?? 0,
            EndColumn = frame.Location?.EndColumn ?? 0,
            InstructionPointerReference = frame.InstructionPointer == null ? null : $"0x{frame.InstructionPointer.Value:X16}",
            ModuleId = moduleId,
            PresentationHint = DebugProtocol.StackFrame.PresentationHintValue.Normal
        };
    }
    public static DebugProtocol.Thread ToThread(this ThreadInfo thread) {
        return new DebugProtocol.Thread(thread.Id, thread.ToDisplayName());
    }
    public static DebugProtocol.Variable ToVariable(this VariableInfo variable) {
        return new DebugProtocol.Variable {
            Name = variable.Name.ToDisplayName(variable.Type),
            Type = variable.Type,
            Value = variable.Value,
            EvaluateName = variable.EvaluateName,
            PresentationHint = variable.ToPresentationHint(),
            VariablesReference = variable.VariablesReference
        };
    }

    public static DebugProtocol.VariablePresentationHint ToPresentationHint(this VariableInfo variable) {
        return new DebugProtocol.VariablePresentationHint {
            Kind = variable.IsError ? null : variable.Kind.ToKindValue(),
            Attributes = variable.IsError ? DebugProtocol.VariablePresentationHint.AttributesValue.FailedEvaluation : null,
            Visibility = variable.Visibility?.ToVisibilityValue(),
        };
    }
    public static DebugProtocol.VariablePresentationHint.KindValue ToKindValue(this VariableKind kind) {
        return kind switch {
            VariableKind.Property => DebugProtocol.VariablePresentationHint.KindValue.Property,
            VariableKind.Group => DebugProtocol.VariablePresentationHint.KindValue.Class,
            _ => DebugProtocol.VariablePresentationHint.KindValue.Data
        };
    }
    public static DebugProtocol.VariablePresentationHint.VisibilityValue ToVisibilityValue(this VariableVisibility visibility) {
        return visibility switch {
            VariableVisibility.Public => DebugProtocol.VariablePresentationHint.VisibilityValue.Public,
            VariableVisibility.Protected => DebugProtocol.VariablePresentationHint.VisibilityValue.Protected,
            VariableVisibility.Internal => DebugProtocol.VariablePresentationHint.VisibilityValue.Internal,
            _ => DebugProtocol.VariablePresentationHint.VisibilityValue.Private
        };
    }
    public static StoppedEvent.ReasonValue ToStoppedReason(this StopReason reason) {
        return reason switch {
            StopReason.Breakpoint => StoppedEvent.ReasonValue.Breakpoint,
            StopReason.Step => StoppedEvent.ReasonValue.Step,
            StopReason.Pause => StoppedEvent.ReasonValue.Pause,
            StopReason.Entry => StoppedEvent.ReasonValue.Entry,
            _ => StoppedEvent.ReasonValue.Unknown
        };
    }
}
