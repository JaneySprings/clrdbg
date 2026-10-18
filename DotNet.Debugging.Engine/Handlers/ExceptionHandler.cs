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
        // With Just My Code the first chance stop is deferred to the USER_FIRST_CHANCE dispatch callback, when the
        // dispatch has reached user code. An exception that never reaches user code does not stop at all under Just My Code
        if (!callbackEvent.Unhandled && JustMyCode) {
            ContinueProcess();
            return;
        }

        var threadId = callbackEvent.Thread.GetId();
        if (callbackEvent.Unhandled)
            exceptionThreads.Remove(threadId);
        // This callback arrives at the raise itself, the thread's frames still show it
        CaptureExceptionModule(callbackEvent.Thread, threadId);
        RaiseExceptionStop(threadId, callbackEvent.Unhandled ? ExceptionStopKind.Unhandled : ExceptionStopKind.FirstChance);
    }
    // Follows the exception dispatch: the first chance stop under Just My Code happens when the dispatch reaches
    // user code, and an exception that passed through user code and is about to be caught beyond it - in non-user
    // code, or by the compiler's catch of an async method it cannot stay in - stops as 'user-unhandled'
    private void HandleExceptionDispatch(Exception2CorDebugManagedCallbackEventArgs callbackEvent) {
        if (IsEvaluating) {
            ContinueProcess();
            return;
        }

        var threadId = callbackEvent.Thread.GetId();
        switch (callbackEvent.DwEventType) {
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_FIRST_CHANCE:
                // The dispatch stops later (entering user code, or heading into a non-user catch), when the
                // thread's frames no longer show the raise - the module the stop names is captured here. An
                // exception that left user code uncaught and crosses a native frame is raised again in the managed
                // caller beyond it (a constructor invoked through reflection, a native callback): that raise
                // continues the first one, whose module stands. Another exception raised there in its place (the
                // wrapper of a failed type initializer) is a raise of its own
                var isUserCodeRaise = IsUserCodeFrame(callbackEvent.Frame);
                var continuesUserCodeRaise = !isUserCodeRaise && exceptionThreads.Contains(threadId) && IsCapturedException(callbackEvent.Thread, threadId);
                if (!continuesUserCodeRaise)
                    CaptureExceptionModule(callbackEvent.Thread, threadId);
                if (isUserCodeRaise)
                    exceptionThreads.Add(threadId);
                break;
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_USER_FIRST_CHANCE:
                exceptionThreads.Add(threadId);
                // Every dispatch of the exception entering user code stops again: an exception propagating
                // through an async chain is rethrown at each await, and each rethrow is a new chance to look at it
                if (JustMyCode && OnExceptionThrown != null) {
                    RaiseExceptionStop(threadId, ExceptionStopKind.FirstChance);
                    return;
                }
                break;
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_CATCH_HANDLER_FOUND:
                var passedThroughUserCode = exceptionThreads.Remove(threadId);
                if (passedThroughUserCode && OnExceptionThrown != null && IsHandlerBeyondUserCode(callbackEvent.Frame, callbackEvent.NOffset)) {
                    RaiseExceptionStop(threadId, ExceptionStopKind.UserUnhandled);
                    return;
                }
                break;
        }
        ContinueProcess();
    }

    private void CaptureExceptionModule(ICorDebugThread thread, int threadId) {
        exceptionModules[threadId] = GetExceptionModuleName(thread);
        exceptionAddresses[threadId] = GetExceptionAddress(thread);
    }
    private bool IsCapturedException(ICorDebugThread thread, int threadId) {
        var address = GetExceptionAddress(thread);
        return address != 0 && exceptionAddresses.GetValueOrDefault(threadId) == address;
    }
    // Zero when the thread's exception cannot be read
    private ulong GetExceptionAddress(ICorDebugThread thread) {
        try {
            if (thread.TryGetCurrentException(out var exception) != Cor.S_OK || exception is not ICorDebugReferenceValue reference)
                return 0;
            return reference.GetValue().Value;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Failed to get the exception address", ex);
            return 0;
        }
    }
    private void RaiseExceptionStop(int threadId, ExceptionStopKind kind) {
        exceptionStopKinds[threadId] = kind;
        // Whether the subscriber continued is recorded by 'Continue' itself, not read back from the runtime:
        // 'IsRunning' can still report the process stopped right after a continue issued inside a callback (the
        // runtime side takes it up later), and a step disabled on that reading is a step lost
        isExceptionStopPending = true;
        OnExceptionThrown!.Invoke(new ExceptionStopInfo(threadId, kind, GetExceptionTypeName(threadId), exceptionModules.GetValueOrDefault(threadId)));
        var stopTaken = isExceptionStopPending;
        isExceptionStopPending = false;
        exceptionStopThreadId = stopTaken ? threadId : null;
        DebuggerLoggingService.LogMessage($"Exception stop ({kind}) on thread {threadId}: {(stopTaken ? "taken by the subscriber" : "continued by the subscriber")}");
        // The subscriber continued: its filters did not match and no stop was taken, so a step in flight
        // (e.g. over an await whose task faulted, or over a call that throws and catches internally)
        // keeps going. A stop that was taken abandons the step instead
        if (stopTaken)
            stepController.Disable();
    }
    private bool IsUserCodeFrame(ICorDebugFrame? frame) {
        return TryClassifyFrame(frame, out var isUserCode) && isUserCode;
    }
    // Only a positively identified non-user handler counts: a throw out of a catch funclet arrives with a null
    // handler frame, and treating that as non-user would stop on exceptions the state machine catches itself
    private bool IsNonUserCodeFrame(ICorDebugFrame? frame) {
        return TryClassifyFrame(frame, out var isUserCode) && !isUserCode;
    }
    // A non-user method's handler, or the catch the compiler puts around the body of an async entry point or 'async void'
    // method: that one only hands the exception to the builder, which rethrows it where no user code can catch it, so the
    // stop is taken here while the frames of the dispatch are still live. The catch of any other async method is a user
    // handler - the awaiting code sees the exception again at its await
    private bool IsHandlerBeyondUserCode(ICorDebugFrame? frame, uint handlerOffset) {
        return IsNonUserCodeFrame(frame) || IsEscapingAsyncCatchHandler(frame, handlerOffset);
    }
    private bool IsEscapingAsyncCatchHandler(ICorDebugFrame? frame, uint handlerOffset) {
        try {
            if (frame is not ICorDebugILFrame ilFrame)
                return false;
            var function = ilFrame.GetFunction();
            var module = FindModule(function.GetModule());
            return module != null && module.MetadataReader.GetAsyncCatchHandlerOffset(function.GetToken()) == handlerOffset;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Failed to classify the catch handler", ex);
            return false;
        }
    }
    // User code is a user module's method that did not opt out through [DebuggerNonUserCode] (under Just My Code),
    // [DebuggerStepThrough] or [DebuggerHidden]: a catch in such a method is exactly what the filter for exceptions
    // leaving user code exists for. False when the frame cannot be resolved
    private bool TryClassifyFrame(ICorDebugFrame? frame, out bool isUserCode) {
        isUserCode = false;
        try {
            if (frame is not ICorDebugILFrame ilFrame)
                return false;
            var function = ilFrame.GetFunction();
            var module = FindModule(function.GetModule());
            if (module == null)
                return false;
            isUserCode = IsUserCodeMethod(module, function.GetToken());
            return true;
        }
        catch {
            return false;
        }
    }
    private bool IsUserCodeMethod(ModuleInfo module, MethodDefToken methodToken) {
        return module.IsUserCode && !module.Module.GetMetaDataInterface<IMetaDataImport>().IsNonUserMethod(methodToken, JustMyCode);
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
    // The module the exception is attributed to in "Exception thrown: '...' in <module>": that of the topmost frame
    // not hidden from stack traces. The runtime raises a fault through a hidden throw helper and rethrows an awaited
    // task's exception through hidden await machinery, both are charged to the method beneath them
    private string? GetExceptionModuleName(ICorDebugThread thread) {
        try {
            string? fallback = null;
            foreach (var frame in thread.GetManagedFrames()) {
                if (frame is not ICorDebugILFrame ilFrame)
                    continue;
                var function = ilFrame.GetFunction();
                var module = FindModule(function.GetModule());
                if (module == null)
                    continue;
                fallback ??= module.Name;
                if (!module.Module.GetMetaDataInterface<IMetaDataImport>().IsStackTraceHidden(function.GetToken()))
                    return module.Name;
            }
            return fallback;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Failed to get the exception module name", ex);
            return null;
        }
    }
    // The stack trace recorded in the exception object: the runtime appends the frames as the dispatch walks them
    // (including the dispatch in flight), a rethrow resets the list, and frames of completed dispatches stay - no
    // walk of the thread's stack could see those anymore. Reading it through ICorDebugExceptionObjectValue also
    // sees the sources, which an evaluation of the StackTrace property cannot. Frames hidden from stack traces are left out
    private string? GetExceptionStackTrace(ICorDebugValue exception) {
        try {
            if (exception.UnwrapDebugValue() is not ICorDebugExceptionObjectValue exceptionObject)
                return null;
            var lines = new List<string>();
            foreach (var frame in exceptionObject.GetExceptionCallStack()) {
                // A frame without a module or a method row cannot be resolved: a dynamic method, or a stub the runtime
                // emitted (the one a reflection invoke calls its target through is recorded with a nil token)
                if (frame.pModule == null || frame.methodDef.IsNil)
                    continue;
                if (frame.pModule.GetMetaDataInterface<IMetaDataImport>().IsStackTraceHidden(frame.methodDef))
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
    // '   at Namespace.Type.Method(int n) in /path/Program.cs:line 50', the source part only with symbols.
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
        var typeName = reader.GetTypeName(method.GetDeclaringType());
        var parameters = reader.GetParameterList(methodDef, DisplayNameSignatureProvider.Instance);
        var line = $"   at {typeName}.{reader.GetString(method.Name)}({parameters})";
        if (location != null)
            line += $" in {location.FilePath}:line {location.Line}";
        return line;
    }
    // The frames of the exception's recorded stack trace that the thread's stack no longer holds, at an exception stop:
    // a rethrow at an await, or after a handler's finally blocks, has unwound the frames of the raise and the recorded
    // trace is where they remain. The runtime records a dispatch as it walks the stack, so the trace ends with live
    // frames: the longest tail matching the top of the stack by module and method is left out. Frames hidden from stack
    // traces and frames without a module (a dynamic method) are skipped, as in the trace itself
    internal List<StackFrameInfo> GetRecordedExceptionFrames(int threadId, List<ICorDebugFrame> liveFrames) {
        var result = new List<StackFrameInfo>();
        if (exceptionStopThreadId != threadId)
            return result;
        try {
            var exception = GetCurrentException(threadId);
            if (exception?.UnwrapDebugValue() is not ICorDebugExceptionObjectValue exceptionObject)
                return result;
            var recorded = exceptionObject.GetExceptionCallStack();
            var deadCount = recorded.Length - CountLiveRecordedFrames(recorded, liveFrames);
            for (var index = 0; index < deadCount; index++) {
                var frame = recorded[index];
                if (frame.pModule == null || frame.methodDef.IsNil)
                    continue;
                var module = FindModule(frame.pModule);
                if (module == null || module.Module.GetMetaDataInterface<IMetaDataImport>().IsStackTraceHidden(frame.methodDef))
                    continue;
                var function = frame.pModule.GetFunctionFromToken(frame.methodDef);
                var frameId = frameReferenceManager.GetOrCreateRecorded(threadId, index);
                var info = new StackFrameInfo(frameId, StackFrameKind.Recorded, function.GetDisplayName(module.MetadataReader.PeMetadataReader));
                info.ModuleName = module.Name;
                info.ModuleId = module.Id;
                info.Location = GetRecordedFrameLocation(frame);
                info.IsUserCode = IsUserCodeMethod(module, frame.methodDef);
                result.Add(info);
            }
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Failed to read the recorded exception frames", ex);
            result.Clear();
        }
        return result;
    }
    // How many of the trailing recorded frames are the top frames of the stack; the methods are resolved once on both
    // sides, a deep recursion leaves thousands of frames to compare
    private int CountLiveRecordedFrames(CorDebugExceptionObjectStackFrame[] recorded, List<ICorDebugFrame> liveFrames) {
        var recordedMethods = new List<string?>(recorded.Length);
        foreach (var frame in recorded)
            recordedMethods.Add(frame.pModule == null ? null : GetMethodKey(FindModule(frame.pModule), frame.methodDef));
        var liveMethods = new List<string?>();
        foreach (var frame in liveFrames) {
            if (frame is not ICorDebugILFrame ilFrame)
                continue;
            var function = ilFrame.GetFunction();
            liveMethods.Add(GetMethodKey(FindModule(function.GetModule()), function.GetToken()));
        }
        for (var count = Math.Min(recordedMethods.Count, liveMethods.Count); count > 0; count--) {
            if (IsLiveTail(recordedMethods, liveMethods, count))
                return count;
        }
        return 0;
    }
    private static bool IsLiveTail(List<string?> recordedMethods, List<string?> liveMethods, int count) {
        for (var index = 0; index < count; index++) {
            var recordedMethod = recordedMethods[recordedMethods.Count - count + index];
            if (recordedMethod == null || recordedMethod != liveMethods[index])
                return false;
        }
        return true;
    }
    // Null for a module the engine does not know
    private static string? GetMethodKey(ModuleInfo? module, MethodDefToken methodDef) {
        return module == null ? null : $"{module.Id}:{methodDef.Value}";
    }
    // A recorded frame holds a native address, mapped back to an IL offset to find the source line
    private SourceLocation? GetRecordedFrameLocation(CorDebugExceptionObjectStackFrame frame) {
        try {
            var module = FindModule(frame.pModule!);
            if (module == null || !module.HasSymbols)
                return null;
            var nativeCode = frame.pModule!.GetFunctionFromToken(frame.methodDef).GetNativeCode();
            if (!nativeCode.TryGetILOffset(frame.ip.Value - nativeCode.GetAddress().Value, out var ilOffset))
                return null;
            return module.MetadataReader.GetSourceLocation(frame.methodDef, ilOffset);
        }
        catch {
            // Not jitted in this code version, or no native view - the frame is listed without a source
            return null;
        }
    }
}
