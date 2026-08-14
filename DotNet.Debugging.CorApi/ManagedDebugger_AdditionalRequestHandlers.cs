using System.Globalization;
using System.Runtime.InteropServices;
using DotNet.Debugging.CorApi.Models.Response;
using ICorDebugSharp;

namespace DotNet.Debugging.CorApi;

public record ModuleLoadedInfo(string ModulePath, int ProcessId, bool SymbolsLoaded, bool IsOptimized);

public enum ExceptionStopKind {
    FirstChance,
    UserUnhandled,
    Unhandled
}

// Request handlers that were not part of the original SharpDbg debugger
public partial class ManagedDebugger {
    /// <summary>
    /// ThreadId, kind of the exception stop. When subscribed, the subscriber decides whether to stop
    /// or continue on an exception (see <see cref="OnStopped"/> fallback in the exception event handler)
    /// </summary>
    public event Action<int, ExceptionStopKind>? OnExceptionThrown;

    /// <summary>
    /// Threads whose current exception was thrown in, or traveled through, user code
    /// </summary>
    private readonly HashSet<int> _exceptionPassedThroughUserCode = new();

    /// <summary>
    /// Tracks the exception dispatch (ICorDebugManagedCallback2::Exception) to detect 'user-unhandled' exceptions:
    /// an exception that passed through user code and is about to be caught in non-user (system) code
    /// </summary>
    private void HandleException2(object? sender, Exception2CorDebugManagedCallbackEventArgs exceptionEventArgs) {
        if (EvalStatus.IsRunning) {
            Continue();
            return;
        }

        var corThread = exceptionEventArgs.Thread;
        switch (exceptionEventArgs.DwEventType) {
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_FIRST_CHANCE:
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_USER_FIRST_CHANCE:
                if (exceptionEventArgs.DwEventType is CorDebugExceptionCallbackType.DEBUG_EXCEPTION_USER_FIRST_CHANCE || IsUserCodeFrame(exceptionEventArgs.Frame))
                    _exceptionPassedThroughUserCode.Add(corThread.Id);
                break;
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_CATCH_HANDLER_FOUND:
                var passedThroughUserCode = _exceptionPassedThroughUserCode.Remove(corThread.Id);
                if (passedThroughUserCode && IsUserCodeFrame(exceptionEventArgs.Frame) is false && OnExceptionThrown is not null) {
                    _asyncStepper?.Disable();
                    if (_stepper is not null) {
                        _stepper.Deactivate();
                        _stepper = null;
                    }
                    OnExceptionThrown.Invoke(corThread.Id, ExceptionStopKind.UserUnhandled);
                    return;
                }
                break;
        }
        Continue();
    }

    private bool IsUserCodeFrame(ICorDebugFrame? frame) {
        try {
            if (frame is not ICorDebugILFrame ilFrame) return false;
            return _modules.TryGetValue(ilFrame.Function.Module.BaseAddress, out var moduleInfo) && moduleInfo.IsUserCode;
        }
        catch {
            return false;
        }
    }

    /// <summary>
    /// Raised alongside <see cref="OnModuleLoaded"/> with the details needed for user-facing module load messages
    /// </summary>
    public event Action<ModuleLoadedInfo>? OnModuleLoadedVerbose;

    private bool _stopAtEntryPending;
    private ICorDebugFunctionBreakpoint? _entryPointBreakpoint;
    private int? _mainThreadId;

    /// <summary>
    /// A readable thread name: the managed 'Thread.Name', 'Main Thread' for the first thread,
    /// the OS-level thread name, or '&lt;No Name&gt;' as the last resort
    /// </summary>
    private string GetThreadDisplayName(ICorDebugThread thread) {
        // A failure to read the managed name (e.g. a thread without a managed Thread object)
        // must not prevent the 'Main Thread' and native name fallbacks
        try {
            var managedName = GetManagedThreadName(thread);
            if (string.IsNullOrEmpty(managedName) is false) return managedName;
        }
        catch (Exception ex) {
            _logger?.Invoke($"Failed to get the managed name of thread {thread.Id}: {ex.Message}");
        }

        if (thread.Id == _mainThreadId) return "Main Thread";

        var nativeName = ThreadNames.GetNativeThreadName(_process?.Id ?? 0, thread.Id);
        if (string.IsNullOrEmpty(nativeName) is false) return nativeName;

        return "<No Name>";
    }

    /// <summary>
    /// Reads the '_name' field of the managed 'System.Threading.Thread' object without any function evaluation
    /// </summary>
    private static string? GetManagedThreadName(ICorDebugThread thread) {
        if (thread.Object?.UnwrapDebugValue() is not ICorDebugObjectValue threadObject) return null;

        var corClass = threadObject.Class;
        var metadataImport = corClass.Module.GetMetaDataInterface<IMetaDataImport>();
        var fieldDef = metadataImport.EnumFieldsWithName(corClass.Token, "_name").SingleOrDefault();
        if (fieldDef.IsNil) return null;

        var fieldValue = threadObject.GetFieldValue(corClass, fieldDef);
        if (fieldValue?.UnwrapDebugValue() is not ICorDebugStringValue stringValue) return null;
        return stringValue.String;
    }

    /// <summary>
    /// Called on module load while a 'stopAtEntry' launch is pending - places a one-shot breakpoint on the assembly entry point
    /// </summary>
    private void TrySetEntryPointBreakpoint(ICorDebugModule corModule, ModuleMetadataReader metadataReader) {
        if (_stopAtEntryPending is false) return;
        var entryPointToken = metadataReader.GetEntryPointMethodToken();
        if (entryPointToken is null) return;

        try {
            var ilOffset = metadataReader.ResolveBreakpointAtMethodEntry(entryPointToken.Value)?.ILOffset ?? 0;
            var function = corModule.GetFunctionFromToken(entryPointToken.Value);
            var corBreakpoint = function.ILCode.CreateBreakpoint(ilOffset);
            corBreakpoint.Activate(true);
            _entryPointBreakpoint = corBreakpoint;
            _stopAtEntryPending = false;
            _logger?.Invoke($"Entry point breakpoint set in {Path.GetFileName(corModule.Name)} at method 0x{entryPointToken.Value:X}, IL offset {ilOffset}");
        }
        catch (Exception ex) {
            _logger?.Invoke($"Failed to set the entry point breakpoint: {ex.Message}");
        }
    }

    private void ClearEntryPointBreakpoint() {
        if (_entryPointBreakpoint is null) return;
        _entryPointBreakpoint.TryActivate(false);
        _entryPointBreakpoint = null;
    }

    /// <summary>
    /// Returns true when the hit breakpoint is the 'stopAtEntry' one and raises the 'entry' stop event.
    /// The entry breakpoint is not tracked by the breakpoint manager, so it is matched by identity or by exclusion
    /// </summary>
    private bool TryHandleEntryPointBreakpoint(ICorDebugThread corThread, ICorDebugFunctionBreakpoint functionBreakpoint) {
        if (_entryPointBreakpoint is null)
            return false;
        if (functionBreakpoint != _entryPointBreakpoint && _breakpointManager.FindByCorBreakpoint(functionBreakpoint) is not null)
            return false;

        ClearEntryPointBreakpoint();
        var sourceInfo = GetSourceInfoAtFrame(corThread.ActiveFrame);
        if (sourceInfo is null) OnStopped?.Invoke(corThread.Id, "entry");
        else OnStopped2?.Invoke(corThread.Id, sourceInfo.Value.FilePath, sourceInfo.Value.StartLine, sourceInfo.Value.StartColumn, "entry", null, sourceInfo.Value.DecompiledSourceInfo);
        return true;
    }

    public int GetTopFrameId(int threadId) {
        return _frameReferenceManager.GetOrCreateFrameId(new ThreadId(threadId), new FrameStackDepth(0));
    }

    public string? GetCurrentExceptionTypeName(int threadId) {
        try {
            var exceptionValue = GetCurrentException(new ThreadId(threadId));
            if (exceptionValue is null) return null;
            return GetValueForCorDebugValue(exceptionValue, false).FriendlyTypeName;
        }
        catch (Exception ex) {
            _logger?.Invoke($"Failed to get the current exception type: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Move the instruction pointer of the thread's active frame to the given source line ('Set Next Statement')
    /// </summary>
    public void SetNextStatement(int threadId, string fileName, int line) {
        if (!_threads.TryGetValue(threadId, out var thread))
            throw new InvalidOperationException($"Thread '{threadId}' not found");
        if (thread.ActiveFrame is not ICorDebugILFrame ilFrame)
            throw new InvalidOperationException("Active frame is not an IL frame");

        var function = ilFrame.Function;
        var module = _modules[function.Module.BaseAddress];
        var resolved = module.MetadataReader.ResolveBreakpoint(fileName, line);
        if (resolved is null)
            throw new InvalidOperationException($"No executable code found at {Path.GetFileName(fileName)}:{line}");
        if (resolved.MethodToken != function.Token)
            throw new InvalidOperationException("The next statement must be within the current method");

        try {
            ilFrame.SetIP(resolved.ILOffset);
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Cannot set the next statement: {ex.Message}");
        }

        // Frames and cached values are neutered after SetIP
        _variableManager.ClearAndDisposeHandleValues();
        _frameReferenceManager.Clear();
    }

    /// <summary>
    /// Assign a new value to a local variable, method argument, object field or array element.
    /// Only primitive values and 'null' for references are supported
    /// </summary>
    public async Task<VariableInfo> SetVariableValue(int variablesReference, string name, string value) {
        var reference = _variableManager.GetReference(variablesReference);
        if (reference is not { } variablesReferenceInfo)
            throw new InvalidOperationException("VariablesReference not found");

        var targetValue = FindVariableValue(variablesReferenceInfo, name);
        if (targetValue is null)
            throw new InvalidOperationException($"Variable '{name}' not found or setting its value is not supported");

        WriteVariableValue(targetValue, value);

        var (friendlyTypeName, displayValue, debuggerProxyInstance, _) = await GetValueForCorDebugValueAsync(targetValue, variablesReferenceInfo.ThreadId, variablesReferenceInfo.FrameStackDepth, true);
        return new VariableInfo {
            Name = name,
            Value = displayValue,
            Type = friendlyTypeName,
            VariablesReference = GetVariablesReference(targetValue, friendlyTypeName, variablesReferenceInfo.ThreadId, variablesReferenceInfo.FrameStackDepth, debuggerProxyInstance)
        };
    }

    private ICorDebugValue? FindVariableValue(VariablesReference reference, string name) {
        if (reference.ReferenceKind is StoredReferenceKind.Scope)
            return FindFrameVariableValue(reference, name);
        if (reference.ObjectValue is null)
            return null;

        var unwrappedValue = reference.ObjectValue.UnwrapDebugValue();
        if (unwrappedValue is ICorDebugArrayValue arrayValue && name.StartsWith('[') && name.EndsWith(']')) {
            if (!uint.TryParse(name.Trim('[', ']'), out var elementIndex))
                return null;
            return arrayValue.GetElement(1, [elementIndex]);
        }
        if (unwrappedValue is ICorDebugObjectValue objectValue) {
            var ilFrame = GetIlFrameForThreadIdAndStackDepth(reference.ThreadId, reference.FrameStackDepth);
            return objectValue.GetClassFieldValue(ilFrame, name);
        }

        return null;
    }

    private ICorDebugValue? FindFrameVariableValue(VariablesReference reference, string name) {
        var ilFrame = GetIlFrameForThreadIdAndStackDepth(reference.ThreadId, reference.FrameStackDepth);
        var function = ilFrame.Function;
        var module = _modules[function.Module.BaseAddress];
        var currentIlOffset = ilFrame.IP.pnOffset;

        foreach (var (index, localVariableValue) in ilFrame.LocalVariables.Index()) {
            var localVariableName = module.MetadataReader.GetLocalVariableName(function.Token, index, currentIlOffset);
            if (localVariableName == name)
                return localVariableValue;
        }

        var metadataImport = function.Module.GetMetaDataInterface<IMetaDataImport>();
        var methodProps = metadataImport.GetMethodProps(function.Token);
        var skipCount = methodProps.pdwAttr.IsMdStatic() ? 0 : 1;
        foreach (var (index, argumentValue) in ilFrame.Arguments.Skip(skipCount).Index()) {
            var paramDef = metadataImport.GetParamForMethodIndex(function.Token, index + 1);
            var paramProps = metadataImport.GetParamProps(paramDef);
            if (paramProps.szName == name)
                return argumentValue;
        }

        return null;
    }

    private static void WriteVariableValue(ICorDebugValue target, string value) {
        var expression = value.Trim();
        if (expression is "null") {
            if (target is not ICorDebugReferenceValue referenceValue)
                throw new InvalidOperationException("Cannot assign 'null' to a value type");

            var hResult = referenceValue.TrySetValue(default);
            if (hResult is not Cor.S_OK)
                throw new InvalidOperationException($"Cannot assign 'null' to the variable: {hResult}");
            return;
        }

        var genericValue = target as ICorDebugGenericValue ?? target.UnwrapDebugValue() as ICorDebugGenericValue;
        if (genericValue is null)
            throw new InvalidOperationException("Only primitive values are supported");

        var bytes = ParsePrimitiveValue(genericValue.Type, expression);
        if (bytes.Length != genericValue.Size)
            throw new InvalidOperationException($"Value size mismatch for type '{genericValue.Type}'");

        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            genericValue.SetValue(buffer);
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static byte[] ParsePrimitiveValue(CorElementType elementType, string value) {
        try {
            return elementType switch {
                CorElementType.BOOLEAN => [(byte)(bool.Parse(value) ? 1 : 0)],
                CorElementType.CHAR => BitConverter.GetBytes(ParseChar(value)),
                CorElementType.I1 => [unchecked((byte)sbyte.Parse(value, CultureInfo.InvariantCulture))],
                CorElementType.U1 => [byte.Parse(value, CultureInfo.InvariantCulture)],
                CorElementType.I2 => BitConverter.GetBytes(short.Parse(value, CultureInfo.InvariantCulture)),
                CorElementType.U2 => BitConverter.GetBytes(ushort.Parse(value, CultureInfo.InvariantCulture)),
                CorElementType.I4 => BitConverter.GetBytes(int.Parse(value, CultureInfo.InvariantCulture)),
                CorElementType.U4 => BitConverter.GetBytes(uint.Parse(value, CultureInfo.InvariantCulture)),
                CorElementType.I8 => BitConverter.GetBytes(long.Parse(value, CultureInfo.InvariantCulture)),
                CorElementType.U8 => BitConverter.GetBytes(ulong.Parse(value, CultureInfo.InvariantCulture)),
                CorElementType.R4 => BitConverter.GetBytes(float.Parse(value, CultureInfo.InvariantCulture)),
                CorElementType.R8 => BitConverter.GetBytes(double.Parse(value, CultureInfo.InvariantCulture)),
                CorElementType.I => IntPtr.Size is 4
                    ? BitConverter.GetBytes(int.Parse(value, CultureInfo.InvariantCulture))
                    : BitConverter.GetBytes(long.Parse(value, CultureInfo.InvariantCulture)),
                CorElementType.U => IntPtr.Size is 4
                    ? BitConverter.GetBytes(uint.Parse(value, CultureInfo.InvariantCulture))
                    : BitConverter.GetBytes(ulong.Parse(value, CultureInfo.InvariantCulture)),
                _ => throw new InvalidOperationException($"Setting values of type '{elementType}' is not supported")
            };
        }
        catch (Exception ex) when (ex is not InvalidOperationException) {
            throw new InvalidOperationException($"Cannot parse value '{value}': {ex.Message}");
        }
    }

    private static char ParseChar(string value) {
        if (value.Length is 3 && value[0] is '\'' && value[2] is '\'')
            return value[1];
        if (value.Length is 1)
            return value[0];

        return (char)ushort.Parse(value, CultureInfo.InvariantCulture);
    }
}