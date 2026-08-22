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
    public static CilValue FromTypeToken(ResolvedCilType type, ICorDebugValue value) {
        return new CilValue(type, value);
    }
    public static CilValue FromLocation(ICilLocation location) {
        return new CilValue(null, null, location);
    }
    // Primitives are read into host values, everything else stays a debuggee value
    public static CilValue FromCorValue(ICorDebugValue value) {
        var primitive = ReadPrimitive(value);
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

    private static object? ReadPrimitive(ICorDebugValue value) {
        if (value.UnwrapDebugValue() is not ICorDebugGenericValue generic)
            return null;
        var data = generic.GetValueAsBytes();
        return generic.GetElementType() switch {
            CorElementType.BOOLEAN => data[0] != 0,
            CorElementType.CHAR => BitConverter.ToChar(data),
            CorElementType.I1 => unchecked((sbyte)data[0]),
            CorElementType.U1 => data[0],
            CorElementType.I2 => BitConverter.ToInt16(data),
            CorElementType.U2 => BitConverter.ToUInt16(data),
            CorElementType.I4 => BitConverter.ToInt32(data),
            CorElementType.U4 => BitConverter.ToUInt32(data),
            CorElementType.I8 => BitConverter.ToInt64(data),
            CorElementType.U8 => BitConverter.ToUInt64(data),
            CorElementType.R4 => BitConverter.ToSingle(data),
            CorElementType.R8 => BitConverter.ToDouble(data),
            CorElementType.I => IntPtr.Size == 8 ? BitConverter.ToInt64(data) : BitConverter.ToInt32(data),
            CorElementType.U => IntPtr.Size == 8 ? BitConverter.ToUInt64(data) : BitConverter.ToUInt32(data),
            _ => null
        };
    }
    // Enums and single-field structs are integers to the interpreter
    private bool TryReadValueTypeInteger(out long value) {
        value = 0;
        if (CorValue?.UnwrapDebugValue() is not ICorDebugGenericValue generic || generic.GetElementType() != CorElementType.VALUETYPE)
            return false;
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
