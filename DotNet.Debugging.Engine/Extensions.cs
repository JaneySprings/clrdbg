using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine;

public static class Extensions {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLiteral(this FieldDefToken fieldDefToken, IMetaDataImport metadataImport) {
        var fieldProps = metadataImport.GetFieldProps(fieldDefToken);
        var isStatic = fieldProps.pdwAttr.IsFdLiteral();
        return isStatic;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsStatic(this FieldDefToken fieldDefToken, IMetaDataImport metadataImport) {
        var fieldProps = metadataImport.GetFieldProps(fieldDefToken);
        var isStatic = fieldProps.pdwAttr.IsFdStatic();
        return isStatic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPublic(this FieldDefToken fieldDefToken, IMetaDataImport metadataImport) {
        var fieldProps = metadataImport.GetFieldProps(fieldDefToken);
        var isPublic = fieldProps.pdwAttr.IsFdPublic();
        return isPublic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPublic(this PropertyToken propertyToken, IMetaDataImport metadataImport) {
        var propertyProps = metadataImport.GetPropertyProps(propertyToken);
        var getterMethodProps = metadataImport.GetMethodProps(propertyProps.pmdGetter);
        var isPublic = getterMethodProps.pdwAttr.IsMdPublic();
        return isPublic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsStatic(this PropertyToken propertyToken, IMetaDataImport metadataImport) {
        var propertyProps = metadataImport.GetPropertyProps(propertyToken);
        var getterMethodProps = metadataImport.GetMethodProps(propertyProps.pmdGetter);
        var isStatic = getterMethodProps.pdwAttr.IsMdStatic();
        return isStatic;
    }

    public static bool HasGetter(this PropertyToken propertyToken, IMetaDataImport metadataImport) {
        return metadataImport.GetPropertyProps(propertyToken).pmdGetter != 0;
    }

    public static bool MatchesVisibility(this FieldDefToken fieldDefToken, IMetaDataImport metadataImport, MemberVisibility visibility) {
        if (visibility is MemberVisibility.All) return true;
        var isPublic = fieldDefToken.IsPublic(metadataImport);
        return visibility is MemberVisibility.Public ? isPublic : isPublic is false;
    }

    public static bool MatchesVisibility(this PropertyToken propertyToken, IMetaDataImport metadataImport, MemberVisibility visibility) {
        if (visibility is MemberVisibility.All) return true;
        if (propertyToken.HasGetter(metadataImport) is false) return false;
        var isPublic = propertyToken.IsPublic(metadataImport);
        return visibility is MemberVisibility.Public ? isPublic : isPublic is false;
    }

    /// <summary>
    /// Compiler generated fields (except hoisted locals) are hidden from the variables view
    /// </summary>
    public static bool IsDisplayable(this FieldDefToken fieldDefToken, IMetaDataImport metadataImport) {
        var fieldName = metadataImport.GetFieldProps(fieldDefToken).szField;
        if (fieldName is null) return false;
        Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameParser.TryParseGeneratedName(fieldName, out var generatedNameKind, out _, out _);
        return generatedNameKind is Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameKind.None or Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameKind.HoistedLocalField;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsStatic(this MethodDefToken methodToken, IMetaDataImport metaDataImport) {
        var methodProps = metaDataImport.GetMethodProps(methodToken);
        var isStatic = methodProps.pdwAttr.IsMdStatic();
        return isStatic;
    }

    // To my knowledge, only strings from CustomAttributes 'Type' ctor use this '+' format
    public static TypeDefToken? FindMaybeNestedTypeDefByNameOrNull(this IMetaDataImport metadataImport, string typeName) {
        var nestedClasses = typeName.Split('+');
        TypeDefToken? enclosingClass = null;
        foreach (var nestedClass in nestedClasses) {
            var typeDefToken = metadataImport.FindTypeDefByNameOrNull(nestedClass, enclosingClass ?? MetadataToken.Nil);
            if (typeDefToken is null) return null;
            enclosingClass = typeDefToken;
        }
        return enclosingClass;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TypeDefToken? FindTypeDefByNameOrNull(this IMetaDataImport metadataImport, string typeName, MetadataToken enclosingClass) {
        var result = metadataImport.TryFindTypeDefByName(typeName, enclosingClass, out var typeDefToken);
        if (result is Cor.S_OK) return typeDefToken;
        return null;
    }

    public static TypeDefToken? FindTypeDefByNameOrNullInCandidateNamespaces(this IMetaDataImport metadataImport, string typeName, MetadataToken enclosingClass, ImmutableArray<string> candidateNamespaces) {
        foreach (var candidateNamespace in candidateNamespaces) {
            var fullTypeName = string.IsNullOrEmpty(candidateNamespace) ? typeName : $"{candidateNamespace}.{typeName}";
            var result = metadataImport.TryFindTypeDefByName(fullTypeName, enclosingClass, out var typeDefToken);
            if (result is Cor.S_OK) return typeDefToken;
        }
        return null;
    }

    // https://github.com/Samsung/netcoredbg/blob/8b8b22200fecdb1aec5f47af63215462d8c79a4b/src/debugger/evaluator.cpp#L695
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCompilerGeneratedFieldName(string fieldName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (fieldName.Length > 1 && fieldName.StartsWith('<')) return true;
        if (fieldName.Length > 4 && fieldName.StartsWith("CS$<", StringComparison.Ordinal)) return true;
        return false;
    }

    public static PropertyToken? GetPropertyWithName(this IMetaDataImport metaDataImport, TypeDefToken typeDefToken, string propertyName) {
        var properties = metaDataImport.EnumProperties(typeDefToken);

        foreach (var property in properties) {
            if (property.IsNil) continue;
            var propertyProps = metaDataImport.GetPropertyProps(property);
            if (propertyProps.szProperty == propertyName) {
                return property;
            }
        }

        return null;
    }

    public static bool HasAnyAttribute(this IMetaDataImport metadataImport, MetadataToken token, string[] attributeNames) {
        foreach (var attributeName in attributeNames) {
            if (metadataImport.TryGetCustomAttributeByName(token, attributeName, out _, out _) is Cor.S_OK) {
                return true;
            }
        }
        return false;
    }

    public static bool IsExtensionMethod(this IMetaDataImport metadataImport, MetadataToken token) {
        return metadataImport.HasAnyAttribute(token, [AttributeConstants.ExtensionMethodAttributeName]);
    }

    public static async Task<ICorDebugValue?> CallParameterlessInstanceMethodAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, ICorDebugFunction corDebugFunction, ICorDebugValue corDebugValue) {
        const bool isStatic = false;

        var typeParameterArgs = corDebugValue.GetExactType().GetTypeParameters();

        // For instance properties, pass the object; for static, pass nothing. Must pass the original CorDebugReferenceValue, not the dereferenced one.
        ICorDebugValue[] corDebugValues = isStatic ? [] : [corDebugValue];
        var result = await eval.CallParameterizedFunctionAsync(processEventsUntilEvalEventFunc, evalStatus, corDebugFunction, typeParameterArgs.Length, typeParameterArgs, corDebugValues.Length, corDebugValues);
        return result;
    }

    public static async Task<ICorDebugValue?> CallParameterizedFunctionAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, ICorDebugFunction corDebugFunction, int typeParamCount, ICorDebugType[]? typeParameterArgs, int paramCount, ICorDebugValue[] corDebugValues, bool throwOnException = false) {
        // Ensure that the object passed in corDebugValues is a CorDebugReferenceValue (when containing object is an instance class), ie must not be dereferenced
        return await RunEvalAsync(eval, processEventsUntilEvalEventFunc, evalStatus, throwOnException,
            () => eval.CallParameterizedFunction(corDebugFunction, typeParamCount, typeParameterArgs, paramCount, corDebugValues),
            e => {
                var getResultResult = e.Eval.TryGetResult(out var result);
                if (getResultResult is not Cor.CORDBG_S_FUNC_EVAL_HAS_NO_RESULT && result is null) Marshal.ThrowExceptionForHR(getResultResult);
                return result;
            });
    }

    public static async Task<ICorDebugValue?> NewParameterizedObjectNoConstructorAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, ICorDebugClass pClass, int nTypeArgs, ICorDebugType[]? ppTypeArgs, bool throwOnException = false) {
        return await RunEvalAsync(eval, processEventsUntilEvalEventFunc, evalStatus, throwOnException,
            () => eval.NewParameterizedObjectNoConstructor(pClass, nTypeArgs, ppTypeArgs),
            e => e.Eval.GetResult());
    }

    public static async Task<ICorDebugValue?> NewParameterizedObjectAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, ICorDebugFunction corDebugFunction, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int argCount, ICorDebugValue[] argValues, bool throwOnException = false) {
        return await RunEvalAsync(eval, processEventsUntilEvalEventFunc, evalStatus, throwOnException,
            () => eval.NewParameterizedObject(corDebugFunction, nTypeArgs, ppTypeArgs, argCount, argValues),
            e => e.Eval.GetResult());
    }

    public static async Task<ICorDebugValue?> NewParameterizedArrayAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, ICorDebugType elementType, uint length, bool throwOnException = false) {
        return await RunEvalAsync(eval, processEventsUntilEvalEventFunc, evalStatus, throwOnException,
            () => eval.NewParameterizedArray(elementType, 1, [length], [0]),
            e => e.Eval.GetResult());
    }

    public static async Task<ICorDebugValue> NewStringAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, string str, bool throwOnException = false) {
        return (await RunEvalAsync(eval, processEventsUntilEvalEventFunc, evalStatus, throwOnException,
            () => eval.NewString(str),
            e => e.Eval.GetResult()))!;
    }

    private static async Task<ICorDebugValue?> RunEvalAsync(ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, bool throwOnException, Action startEval, Func<EvalCompleteCorDebugManagedCallbackEventArgs, ICorDebugValue?> onComplete) {
        ICorDebugValue? returnValue = null;

        startEval();

        evalStatus.IsRunning = true;
        try {
            eval.GetThread().GetProcess().Continue(false);
            var evalEvent = await processEventsUntilEvalEventFunc();
            if (evalEvent is EvalCompleteCorDebugManagedCallbackEventArgs completeEvent) {
                if (completeEvent.Eval != eval) throw new ManagedDebugger.EvalException("EvalComplete callback error - Eval does not match");
                returnValue = onComplete(completeEvent);
            }
            else if (evalEvent is EvalExceptionCorDebugManagedCallbackEventArgs exceptionEvent) {
                if (exceptionEvent.Eval != eval) throw new ManagedDebugger.EvalException("EvalException callback error - Eval does not match");
                var exceptionValue = exceptionEvent.Eval.GetResult() ?? throw new ManagedDebugger.EvalException("EvalException callback error - Result is null");
                if (throwOnException) {
                    try {
                        throw new ManagedDebugger.EvalException($"Evaluation threw {ManagedDebugger.GetCorDebugTypeFriendlyName(exceptionValue.GetExactType())}");
                    }
                    finally {
                        if (exceptionValue is ICorDebugHandleValue handle) handle.TryDispose();
                    }
                }
                returnValue = exceptionValue;
            }
            return returnValue;
        }
        finally {
            evalStatus.IsRunning = false;
        }
    }

    public static ICorDebugValue NewBooleanValue(this ICorDebugEval eval, bool value) {
        var corValue = eval.CreateValue(CorElementType.BOOLEAN, null);

        if (value is true && corValue is ICorDebugGenericValue genValue) {
            var size = genValue.GetSize();
            var valueData = new byte[size];
            valueData[0] = 1;
            unsafe {
                fixed (byte* p = valueData) {
                    var ptr = (IntPtr)p;
                    genValue.SetValue(ptr);
                }
            }
        }

        return corValue;
    }

    public static string ToDisplayName(this CorDebugInternalFrameType frameType) {
        return frameType switch {
            CorDebugInternalFrameType.STUBFRAME_M2U => "[Managed to Native Transition]",
            CorDebugInternalFrameType.STUBFRAME_U2M => "[Native to Managed Transition]",
            CorDebugInternalFrameType.STUBFRAME_APPDOMAIN_TRANSITION => "[Appdomain Transition]",
            CorDebugInternalFrameType.STUBFRAME_LIGHTWEIGHT_FUNCTION => "[Lightweight function]",
            CorDebugInternalFrameType.STUBFRAME_FUNC_EVAL => "[Func Eval]",
            CorDebugInternalFrameType.STUBFRAME_INTERNALCALL => "[Internal Call]",
            CorDebugInternalFrameType.STUBFRAME_CLASS_INIT => "[Class Init]",
            CorDebugInternalFrameType.STUBFRAME_EXCEPTION => "[Exception]",
            CorDebugInternalFrameType.STUBFRAME_SECURITY => "[Security]",
            CorDebugInternalFrameType.STUBFRAME_JIT_COMPILATION => "[JIT Compilation]",
            _ => "[Unknown]"
        };
    }
}