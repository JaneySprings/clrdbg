using System.Reflection.Metadata.Ecma335;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Metadata;
using DotNet.Debugging.Engine.Models;
using DotNet.Debugging.Engine.Variables;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    // First-chance and unhandled exceptions. The subscriber applies its filters and continues when the exception is not interesting
    private void HandleException(ExceptionCorDebugManagedCallbackEventArgs callbackEvent) {
        if (IsEvaluating || OnExceptionThrown == null) {
            ContinueProcess();
            return;
        }
        // With Just My Code the first chance stop is deferred to the USER_FIRST_CHANCE dispatch callback,
        // where Microsoft's debugger stops: the exception's recorded stack trace has reached user code
        // there. An exception that never reaches user code does not stop at all under Just My Code
        if (!callbackEvent.Unhandled && JustMyCode) {
            ContinueProcess();
            return;
        }

        var threadId = callbackEvent.Thread.GetId();
        if (callbackEvent.Unhandled)
            exceptionThreads.Remove(threadId);
        // This callback arrives at the raise itself, the thread's frames still show it
        exceptionModules[threadId] = GetExceptionModuleName(callbackEvent.Thread);
        RaiseExceptionStop(threadId, callbackEvent.Unhandled ? ExceptionStopKind.Unhandled : ExceptionStopKind.FirstChance);
    }
    // Follows the exception dispatch: the first chance stop under Just My Code happens when the dispatch reaches
    // user code, and an exception that passed through user code and is about to be caught in non-user code
    // stops as 'user-unhandled'
    private void HandleExceptionDispatch(Exception2CorDebugManagedCallbackEventArgs callbackEvent) {
        if (IsEvaluating) {
            ContinueProcess();
            return;
        }

        var threadId = callbackEvent.Thread.GetId();
        switch (callbackEvent.DwEventType) {
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_FIRST_CHANCE:
                // The dispatch stops later (entering user code, or heading into a non-user catch), when the
                // thread's frames no longer show the raise - the module the stop names is captured here
                exceptionModules[threadId] = GetExceptionModuleName(callbackEvent.Thread);
                if (IsUserCodeFrame(callbackEvent.Frame))
                    exceptionThreads.Add(threadId);
                break;
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_USER_FIRST_CHANCE:
                exceptionThreads.Add(threadId);
                // Every dispatch of the exception entering user code stops again, the way Microsoft's
                // debugger re-breaks on each rethrow of an exception propagating through an async chain
                if (JustMyCode && OnExceptionThrown != null) {
                    RaiseExceptionStop(threadId, ExceptionStopKind.FirstChance);
                    return;
                }
                break;
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_CATCH_HANDLER_FOUND:
                var passedThroughUserCode = exceptionThreads.Remove(threadId);
                if (passedThroughUserCode && IsNonUserCodeFrame(callbackEvent.Frame) && OnExceptionThrown != null) {
                    RaiseExceptionStop(threadId, ExceptionStopKind.UserUnhandled);
                    return;
                }
                break;
        }
        ContinueProcess();
    }

    private void RaiseExceptionStop(int threadId, ExceptionStopKind kind) {
        exceptionStopKinds[threadId] = kind;
        OnExceptionThrown!.Invoke(new ExceptionStopInfo(threadId, kind, GetExceptionTypeName(threadId), exceptionModules.GetValueOrDefault(threadId)));
        // The subscriber continued: its filters did not match and no stop was taken, so a step in flight
        // (e.g. over an await whose task faulted, or over a call that throws and catches internally)
        // keeps going. A stop that was taken abandons the step instead
        if (!IsRunning)
            stepController.Disable();
    }
    private bool IsUserCodeFrame(ICorDebugFrame? frame) {
        return GetFrameModule(frame) is { IsUserCode: true };
    }
    // Only a positively identified non-user handler counts: a throw out of a catch funclet arrives with a null
    // handler frame, and treating that as non-user would stop on exceptions the state machine catches itself
    private bool IsNonUserCodeFrame(ICorDebugFrame? frame) {
        return GetFrameModule(frame) is { IsUserCode: false };
    }
    private ModuleInfo? GetFrameModule(ICorDebugFrame? frame) {
        try {
            if (frame is not ICorDebugILFrame ilFrame)
                return null;
            return FindModule(ilFrame.GetFunction().GetModule());
        }
        catch {
            return null;
        }
    }
    private string? GetExceptionTypeName(int threadId) {
        try {
            var exception = GetCurrentException(threadId);
            return exception == null ? null : ValueFormatter.Format(exception, false).TypeName;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Failed to get the current exception type", ex);
            return null;
        }
    }
    // The module the exception is attributed to in "Exception thrown: '...' in <module>": the topmost stack
    // frame whose type is not [StackTraceHidden]. Microsoft's debugger attributes a fault raised through the runtime's
    // throw helper (a hidden type) to the user method that faulted, yet names the core library when its
    // await machinery rethrows - those methods carry the attribute themselves, and still count. The mirror
    // image of the recorded trace, which drops method-hidden frames and keeps type-hidden ones
    private string? GetExceptionModuleName(ICorDebugThread thread) {
        try {
            string? fallback = null;
            foreach (var frame in EnumerateFrames(thread)) {
                if (frame is not ICorDebugILFrame ilFrame)
                    continue;
                var function = ilFrame.GetFunction();
                var module = FindModule(function.GetModule());
                if (module == null)
                    continue;
                fallback ??= module.Name;
                var metadataImport = module.Module.GetMetaDataInterface<IMetaDataImport>();
                if (!metadataImport.HasAttribute(metadataImport.GetMethodProps(function.GetToken()).pClass, AttributeNames.StackTraceHidden))
                    return module.Name;
            }
            return fallback;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Failed to get the exception module name", ex);
            return null;
        }
    }
    // The stack trace recorded in the exception object, the list Microsoft's debugger shows: the runtime appends the frames as
    // the dispatch walks them (including the dispatch in flight), a rethrow resets the list, and frames of
    // completed dispatches stay - no walk of the thread's stack could see those anymore. Reading it through
    // ICorDebugExceptionObjectValue also sees the sources, which an evaluation of the StackTrace property cannot.
    // Frames whose method is marked [StackTraceHidden] are dropped the way Microsoft's debugger drops them
    // (a method hidden through its type alone, like the runtime's throw helpers, stays listed)
    private string? GetExceptionStackTrace(ICorDebugValue exception) {
        try {
            if (exception.UnwrapDebugValue() is not ICorDebugExceptionObjectValue exceptionObject)
                return null;
            var lines = new List<string>();
            foreach (var frame in exceptionObject.GetExceptionCallStack()) {
                // A frame without a module cannot be resolved (e.g. a dynamic method)
                if (frame.pModule == null)
                    continue;
                if (frame.pModule.GetMetaDataInterface<IMetaDataImport>().HasAttribute(frame.methodDef, AttributeNames.StackTraceHidden))
                    continue;
                lines.Add(FormatStackTraceLine(frame.pModule, frame.methodDef, GetRecordedFrameLocation(frame)));
            }
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Failed to read the exception stack trace", ex);
            return null;
        }
    }
    // '   at Namespace.Type.Method(Int32 n) in /path/Program.cs:line 50', the source part only with symbols.
    // The reflection reader qualifies a nested type with its enclosing chain ('SafeExtensions.<InvokeAsync>d__6'),
    // the form the async state machine frames of a recorded trace are shown in
    private string FormatStackTraceLine(ICorDebugModule corModule, MethodDefToken methodDef, SourceLocation? location) {
        var module = FindModule(corModule);
        if (module == null) {
            var methodProps = corModule.GetMetaDataInterface<IMetaDataImport>().GetMethodProps(methodDef);
            return $"   at {methodProps.szMethod}()";
        }
        var reader = module.MetadataReader.PeMetadataReader;
        var method = reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(methodDef));
        var typeName = TypeNameSignatureProvider.GetTypeName(reader, method.GetDeclaringType());
        var parameters = GetParameterList(reader, methodDef, StackTraceSignatureProvider.Instance);
        var line = $"   at {typeName}.{reader.GetString(method.Name)}({parameters})";
        if (location != null)
            line += $" in {location.FilePath}:line {location.Line}";
        return line;
    }
    // A recorded frame holds a native address, mapped back to an IL offset to find the source line
    private SourceLocation? GetRecordedFrameLocation(CorDebugExceptionObjectStackFrame frame) {
        try {
            var module = FindModule(frame.pModule!);
            if (module == null || !module.HasSymbols)
                return null;
            var nativeCode = frame.pModule!.GetFunctionFromToken(frame.methodDef).GetNativeCode();
            var nativeOffset = frame.ip.Value - nativeCode.GetAddress().Value;
            foreach (var entry in nativeCode.GetILToNativeMapping()) {
                if (nativeOffset < entry.nativeStartOffset || nativeOffset >= entry.nativeEndOffset)
                    continue;
                // The special offsets of a prolog or epilog (-2, -3) have no source mapped to them
                if ((int)entry.ilOffset < 0)
                    return null;
                return module.MetadataReader.GetSourceLocation(frame.methodDef, (int)entry.ilOffset);
            }
            return null;
        }
        catch {
            // Not jitted in this code version, or no native view - the frame is listed without a source
            return null;
        }
    }
}
