using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;

namespace DotNet.Debugging.Engine.Evaluation;

// A storage location the interpreter can read and write: a debuggee variable, a host temporary or a synthetic variable
internal interface ICilLocation {
    CilValue Read();
    void Write(CilValue value);
}

internal class CorDebugLocation : ICilLocation {
    public ICorDebugValue Value { get; }

    public CorDebugLocation(ICorDebugValue value) {
        Value = value;
    }

    public CilValue Read() {
        return CilValue.FromCorValue(GetStorageValue());
    }
    public void Write(CilValue source) {
        var storage = GetStorageValue();
        var destination = storage.UnwrapDebugValue();

        // Value-typed storage. Value-type locations may be surfaced as an ICorDebugReferenceValue (e.g. enum locals),
        // so the discriminating signal is the dereferenced destination being a value-type generic, not whether the storage itself is a reference
        if (destination is ICorDebugGenericValue destinationGeneric && IsValueType(destinationGeneric.GetElementType())) {
            if (source.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric) {
                if (destinationGeneric.GetSize() != sourceGeneric.GetSize())
                    throw new InvalidOperationException("CIL value sizes do not match");
                destinationGeneric.SetValueFromBytes(sourceGeneric.GetValueAsBytes());
                return;
            }
            if (source.Value != null) {
                var data = destinationGeneric.GetElementType() == CorElementType.VALUETYPE
                    ? CilValueEncoding.GetBytes(source.Value, destinationGeneric.GetSize())
                    : CilValueEncoding.GetBytes(source.Value, destinationGeneric.GetElementType());
                destinationGeneric.SetValueFromBytes(data);
                return;
            }
            if (source.IsNull) {
                destinationGeneric.SetValueFromBytes(new byte[destinationGeneric.GetSize()]);
                return;
            }
            throw new NotSupportedException("The CIL value cannot be stored in this debuggee location");
        }

        // Reference-typed location (string/class/object/array local, field or element). The value is written into the
        // slot itself, never into the dereferenced target, and null zeroes the slot. The dereferenced destination of a
        // non-null string slot is the string's data (generic STRING), which must never be written as bytes
        if (storage is ICorDebugReferenceValue destinationReference) {
            if (source.IsNull) {
                destinationReference.SetValue(default);
                return;
            }
            if (source.CorValue is ICorDebugReferenceValue sourceReference) {
                destinationReference.SetValue(sourceReference.GetValue());
                return;
            }
            if (source.CorValue is ICorDebugHeapValue2 sourceHeap) {
                var handle = sourceHeap.CreateHandle(CorDebugHandleType.HANDLE_STRONG);
                try {
                    destinationReference.SetValue(handle.GetValue());
                }
                finally {
                    handle.TryDispose();
                }
                return;
            }
            throw new NotSupportedException("Cannot store a non-reference CIL value in a reference debuggee location");
        }

        throw new NotSupportedException("The CIL value cannot be stored in this debuggee location");
    }

    private ICorDebugValue GetStorageValue() {
        if (Value is ICorDebugReferenceValue byRef && byRef.GetElementType() == CorElementType.BYREF)
            return byRef.Dereference();
        return Value;
    }
    private static bool IsValueType(CorElementType elementType) {
        return elementType is CorElementType.VALUETYPE or CorElementType.BOOLEAN or CorElementType.CHAR
            or CorElementType.I1 or CorElementType.U1 or CorElementType.I2 or CorElementType.U2
            or CorElementType.I4 or CorElementType.U4 or CorElementType.I8 or CorElementType.U8
            or CorElementType.R4 or CorElementType.R8 or CorElementType.I or CorElementType.U;
    }
}

internal class TemporaryLocation : ICilLocation {
    private CilValue value;

    public TemporaryLocation(CilValue initialValue) {
        value = initialValue;
    }

    public CilValue Read() {
        return value;
    }
    public void Write(CilValue newValue) {
        value = newValue;
    }
}

// A variable declared by the expression itself, stored in a single-element array allocated in the debuggee
internal class SyntheticVariableLocation : ICilLocation {
    public ICorDebugValue ArrayReference { get; }
    public ICorDebugValue StorageValue => ((ICorDebugArrayValue)ArrayReference.UnwrapDebugValue()).GetElementAtPosition(0);

    public SyntheticVariableLocation(ICorDebugValue arrayReference) {
        ArrayReference = arrayReference;
    }

    public CilValue Read() {
        return CilValue.FromCorValue(StorageValue);
    }
    public void Write(CilValue value) {
        new CorDebugLocation(StorageValue).Write(value);
    }
}

internal static class CilValueEncoding {
    public static byte[] GetBytes(object value, int size) {
        return size switch {
            1 => [unchecked((byte)Convert.ToInt64(value))],
            2 => BitConverter.GetBytes(unchecked((short)Convert.ToInt64(value))),
            4 => BitConverter.GetBytes(unchecked((int)Convert.ToInt64(value))),
            8 => BitConverter.GetBytes(Convert.ToInt64(value)),
            _ => throw new NotSupportedException($"Cannot encode a primitive CIL value into a {size}-byte value type")
        };
    }
    public static byte[] GetBytes(object value, CorElementType targetType) {
        return targetType switch {
            CorElementType.BOOLEAN => [(bool)value ? (byte)1 : (byte)0],
            CorElementType.CHAR => BitConverter.GetBytes(Convert.ToChar(value)),
            CorElementType.I1 => [unchecked((byte)Convert.ToSByte(value))],
            CorElementType.U1 => [Convert.ToByte(value)],
            CorElementType.I2 => BitConverter.GetBytes(Convert.ToInt16(value)),
            CorElementType.U2 => BitConverter.GetBytes(Convert.ToUInt16(value)),
            CorElementType.I4 => BitConverter.GetBytes(Convert.ToInt32(value)),
            CorElementType.U4 => BitConverter.GetBytes(Convert.ToUInt32(value)),
            CorElementType.I8 => BitConverter.GetBytes(Convert.ToInt64(value)),
            CorElementType.U8 => BitConverter.GetBytes(Convert.ToUInt64(value)),
            CorElementType.R4 => BitConverter.GetBytes(Convert.ToSingle(value)),
            CorElementType.R8 => BitConverter.GetBytes(Convert.ToDouble(value)),
            _ => throw new NotSupportedException($"Cannot encode a primitive CIL value as '{targetType}'")
        };
    }
}
