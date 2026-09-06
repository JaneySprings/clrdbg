using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;

namespace DotNet.Debugging.Engine.Evaluation;

// A value on the interpreter's evaluation stack: a host primitive ('Value'), a debuggee value ('CorValue'),
// a type token (a 'ResolvedCilType' in 'Value') or the address of a storage location ('Location')
internal class CilValue {
    public object? Value { get; }
    public ICorDebugValue? CorValue { get; }
    public ICilLocation? Location { get; }
    public bool IsNull => Value == null && (CorValue == null || (CorValue is ICorDebugReferenceValue reference && reference.IsNull()));

    private CilValue(object? value, ICorDebugValue? corValue, ICilLocation? location = null) {
        Value = value;
        CorValue = corValue;
        Location = location;
    }

    public static CilValue Null() {
        return new CilValue(null, null);
    }
    public static CilValue FromPrimitive(object value) {
        return new CilValue(value, null);
    }
    // A value that only exists in the debugger: a host object, delegate or sequence
    public static CilValue FromHostValue(object value) {
        return new CilValue(value, null);
    }
    public static CilValue FromTypeToken(ResolvedCilType type, ICorDebugValue value) {
        return new CilValue(type, value);
    }
    public static CilValue FromLocation(ICilLocation location) {
        return new CilValue(null, null, location);
    }
    // Primitives are read into host values, everything else stays a debuggee value
    public static CilValue FromCorValue(ICorDebugValue value) {
        var primitive = value.ReadPrimitive();
        return primitive == null ? new CilValue(null, value) : new CilValue(primitive, null);
    }
    // Wraps a debuggee value without collapsing primitives (e.g. strings) to host values, for values being stored into the debuggee
    public static CilValue FromDebuggeeValue(ICorDebugValue value) {
        return new CilValue(null, value);
    }

    public string? GetStringText() {
        if (Value is string text)
            return text;
        return (CorValue?.UnwrapDebugValue() as ICorDebugStringValue)?.GetString();
    }
    public CilValue Dereference() {
        return Location?.Read() ?? throw new InvalidOperationException("The CIL value is not a managed location");
    }

    public int AsInt32() {
        switch (Value) {
            case bool value: return value ? 1 : 0;
            case char value: return value;
            case sbyte value: return value;
            case byte value: return value;
            case short value: return value;
            case ushort value: return value;
            case int value: return value;
            case uint value: return unchecked((int)value);
        }
        if (TryReadValueTypeInteger(out var integer))
            return unchecked((int)integer);
        throw new InvalidOperationException($"Value '{Value?.GetType().Name ?? "null"}' is not an int32 stack value");
    }
    public long AsInt64() {
        switch (Value) {
            case long value: return value;
            case ulong value: return unchecked((long)value);
        }
        if (TryReadValueTypeInteger(out var integer))
            return integer;
        return AsInt32();
    }
    public ulong AsUInt64() {
        switch (Value) {
            case byte value: return value;
            case ushort value: return value;
            case uint value: return value;
            case ulong value: return value;
            case sbyte value: return unchecked((ulong)value);
            case short value: return unchecked((ulong)value);
            case int value: return unchecked((uint)value);
            case long value: return unchecked((ulong)value);
            case bool value: return value ? 1UL : 0UL;
            case char value: return value;
        }
        if (TryReadValueTypeInteger(out var integer))
            return unchecked((ulong)integer);
        throw new InvalidOperationException($"Value '{Value?.GetType().Name ?? "null"}' is not an integer stack value");
    }
    public double AsFloat() {
        switch (Value) {
            case float value: return value;
            case double value: return value;
        }
        throw new InvalidOperationException($"Value '{Value?.GetType().Name ?? "null"}' is not a floating-point stack value");
    }
    public bool TryGetInt64(out long value) {
        try {
            value = AsInt64();
            return true;
        }
        catch (InvalidOperationException) {
            value = 0;
            return false;
        }
    }
    public bool IsTrue() {
        if (CorValue is ICorDebugReferenceValue reference)
            return !reference.IsNull();
        if (CorValue != null)
            return true;
        if (Value is HostObject or HostDelegate or HostSequence or HostFunction or HostSpan)
            return true;
        switch (Value) {
            case null: return false;
            case bool value: return value;
            case float value: return value != 0;
            case double value: return value != 0;
            case long value: return value != 0;
            case ulong value: return value != 0;
            case string: return true;
        }
        return AsInt32() != 0;
    }

    // Enums and single-field structs are integers to the interpreter. An enum is read through its 'value__' field,
    // whose primitive type carries the sign of the underlying type (an 'sbyte' member -1 must not read as 255)
    private bool TryReadValueTypeInteger(out long value) {
        value = 0;
        if (CorValue?.UnwrapDebugValue() is not ICorDebugGenericValue generic || generic.GetElementType() != CorElementType.VALUETYPE)
            return false;
        if (generic is ICorDebugObjectValue objectValue && objectValue.TryReadEnumValue(out value))
            return true;
        var data = generic.GetValueAsBytes();
        switch (data.Length) {
            case 1: value = data[0]; return true;
            case 2: value = BitConverter.ToInt16(data); return true;
            case 4: value = BitConverter.ToInt32(data); return true;
            case 8: value = BitConverter.ToInt64(data); return true;
        }
        return false;
    }
}
