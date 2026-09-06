using System.Reflection.Emit;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Evaluation;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CilValueExtensions {
    // The value a location holds, the value itself when it is not a location
    public static CilValue DereferenceLocation(this CilValue value) {
        return value.Location != null ? value.Dereference() : value;
    }
    // A boxed primitive as the host primitive; any other value as it is. The runtime reports the object of a box as a
    // VALUETYPE, which 'FromCorValue' keeps as a debuggee value, so the host arithmetic asks for the primitive itself
    public static CilValue UnboxPrimitive(this CilValue value) {
        if (value.Value != null || value.CorValue == null)
            return value;
        if (value.CorValue.UnwrapDebugValue() is not ICorDebugGenericValue generic || generic.GetElementType() != CorElementType.VALUETYPE)
            return value;
        var elementType = generic.GetExactType().GetPrimitiveElementType();
        if (elementType == null)
            return value;
        var primitive = generic.ReadPrimitive(elementType.Value);
        return primitive == null ? value : CilValue.FromPrimitive(primitive);
    }
    public static bool IsHostValue(this CilValue value) {
        return value.Value is HostObject or HostDelegate or HostSequence or HostFunction or HostSpan;
    }
    // Whether the value references an object on the debuggee's heap (a string, a class instance, an array) rather
    // than a boxed value or a primitive
    public static bool IsHeapObjectReference(this CilValue value) {
        if (value.CorValue is not ICorDebugReferenceValue reference || reference.IsNull())
            return false;
        var target = reference.Dereference();
        return target is ICorDebugStringValue or ICorDebugArrayValue || (target is ICorDebugObjectValue && target is not ICorDebugBoxValue);
    }

    public static HostObject GetHostObject(this CilValue receiver) {
        receiver = receiver.DereferenceLocation();
        if (receiver.IsNull)
            throw new NullReferenceException();
        return receiver.Value as HostObject ?? throw new InvalidOperationException("The field belongs to a type the expression declares, the receiver is not an object of it");
    }
    public static ICorDebugArrayValue GetArrayValue(this CilValue value) {
        return value.CorValue?.UnwrapDebugValue() as ICorDebugArrayValue ?? throw new NullReferenceException("The array reference is null");
    }
    public static ICorDebugArrayValue GetArrayValue(this ICorDebugValue value) {
        return value.UnwrapDebugValue() as ICorDebugArrayValue ?? throw new NullReferenceException("The array reference is null");
    }
    public static ICorDebugObjectValue GetFieldReceiver(this CilValue receiver) {
        var corValue = receiver.CorValue;
        if (corValue == null && receiver.Location is CorDebugLocation directLocation)
            corValue = directLocation.Value;
        else if (corValue == null && receiver.Location != null)
            corValue = receiver.Location.Read().CorValue;
        return corValue?.UnwrapDebugValueToObject() ?? throw new NullReferenceException("The instance field receiver is null");
    }
    public static ICorDebugBoxValue GetBoxedValue(this CilValue source) {
        source = source.DereferenceLocation();
        if (source.IsNull)
            throw new NullReferenceException();
        var boxed = source.CorValue is ICorDebugReferenceValue reference
            ? reference.Dereference() as ICorDebugBoxValue
            : source.CorValue as ICorDebugBoxValue;
        return boxed ?? throw new InvalidCastException("The CIL value is not boxed");
    }

    public static CilValue Negate(this CilValue value) {
        if (value.Value is float or double)
            return CilValue.FromPrimitive(-value.AsFloat());
        if (value.Value is long or ulong)
            return CilValue.FromPrimitive(-value.AsInt64());
        return CilValue.FromPrimitive(-value.AsInt32());
    }
    // The order Comparer<T>.Default gives: null first, strings by the current culture, numbers by value
    public static int CompareValue(this CilValue left, CilValue right, bool unsigned = false) {
        if (left.IsNull || right.IsNull)
            return left.IsNull ? (right.IsNull ? 0 : -1) : 1;
        var leftText = left.GetStringText();
        var rightText = right.GetStringText();
        if (leftText != null && rightText != null)
            return string.Compare(leftText, rightText, StringComparison.CurrentCulture);
        left = left.UnboxPrimitive();
        right = right.UnboxPrimitive();
        // An unsigned value is held as its own type when it was read from the debuggee, as its signed twin when the
        // interpreter computed it (the CIL stack has no unsigned int32) - the caller then knows the static type
        var isUnsigned = unsigned || left.Value is uint or ulong || right.Value is uint or ulong;
        if (isUnsigned ? OpCodes.Clt_Un.Compare(left, right) : OpCodes.Clt.Compare(left, right))
            return -1;
        if (isUnsigned ? OpCodes.Cgt_Un.Compare(left, right) : OpCodes.Cgt.Compare(left, right))
            return 1;
        return 0;
    }
    // The equality EqualityComparer<T>.Default gives: strings by text, values by content, references by identity
    public static bool ValueEquals(this CilValue left, CilValue right) {
        if (left.IsNull || right.IsNull)
            return left.IsNull && right.IsNull;
        var leftText = left.GetStringText();
        var rightText = right.GetStringText();
        if (leftText != null || rightText != null)
            return leftText == rightText;
        left = left.UnboxPrimitive();
        right = right.UnboxPrimitive();
        if (left.Value == null && right.Value == null
                && left.CorValue!.UnwrapDebugValue() is ICorDebugGenericValue leftGeneric && left.CorValue is not ICorDebugReferenceValue
                && right.CorValue!.UnwrapDebugValue() is ICorDebugGenericValue rightGeneric && right.CorValue is not ICorDebugReferenceValue)
            return leftGeneric.GetValueAsBytes().AsSpan().SequenceEqual(rightGeneric.GetValueAsBytes());
        return OpCodes.Ceq.Compare(left, right);
    }

    // The 'count' values on top of the stack in push order, the last pushed last
    public static CilValue[] PopArguments(this Stack<CilValue> stack, int count) {
        var arguments = new CilValue[count];
        for (var i = count - 1; i >= 0; i--)
            arguments[i] = stack.Pop();
        return arguments;
    }
}
