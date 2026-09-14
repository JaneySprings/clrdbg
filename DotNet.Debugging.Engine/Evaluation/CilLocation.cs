using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;

namespace DotNet.Debugging.Engine.Evaluation;

// A storage location the interpreter can read and write: a debuggee variable, a host temporary or a synthetic variable
internal interface ICilLocation {
    CilValue Read();
    void Write(CilValue value);
}

// A frame slot the runtime cannot read at the current instruction, the variable was optimized away
internal class UnavailableLocation : ICilLocation {
    public const string Message = "The value is not available at this instruction, it may have been optimized away";

    public CilValue Read() {
        throw new EvaluationException(Message);
    }
    public void Write(CilValue value) {
        throw new EvaluationException(Message);
    }
}

internal class CorDebugLocation : ICilLocation {
    private readonly ICorDebugValue? value;
    private readonly Func<ICorDebugValue>? fetch;

    // Fetched anew on every access when the location was created with a fetch (a frame slot): the value object of a
    // value type is a snapshot, and the debuggee's memory behind it changes under an instance call on the slot
    public ICorDebugValue Value => fetch != null ? fetch() : value!;
    // A slot whose static type is a reference type: an 'object', class, string or array local, field or element
    public bool IsReferenceSlot {
        get {
            if (GetStorageValue() is not ICorDebugReferenceValue reference)
                return false;
            if (reference.GetElementType() is not (CorElementType.CLASS or CorElementType.OBJECT or CorElementType.STRING or CorElementType.SZARRAY or CorElementType.ARRAY))
                return false;
            // A value-type location surfaced as a reference (an enum local) dereferences straight to its value, a
            // reference slot to a heap object - a box included
            if (!reference.IsNull() && reference.Dereference() is ICorDebugGenericValue target && target.GetElementType() == CorElementType.VALUETYPE)
                return false;
            return true;
        }
    }

    public CorDebugLocation(ICorDebugValue value) {
        this.value = value;
    }
    public CorDebugLocation(Func<ICorDebugValue> fetch) {
        this.fetch = fetch;
    }

    // A by-reference slot (a 'ref' parameter or local, the 'this' of a struct method) holds the address of a
    // variable: the IL reads and writes it through ldind/stind/ldobj, so the slot yields the location it points to
    public CilValue Read() {
        if (Value is ICorDebugReferenceValue byRef && byRef.GetElementType() == CorElementType.BYREF)
            return CilValue.FromLocation(new CorDebugLocation(byRef.Dereference()));
        return CilValue.FromCorValue(Value);
    }
    public void Write(CilValue source) {
        var storage = GetStorageValue();
        // A reference slot takes the reference (or null) whatever it holds now: an 'object' slot holding a box unwraps to
        // the boxed value, and writing the source's bytes into that box would change every other reference to it rather
        // than assign the slot. A heap object (a string, a class instance, an array) is stored by reference everywhere;
        // values (a boxed enum or struct the evaluation produced, a host primitive) copy their bytes into a value slot
        if (storage is ICorDebugReferenceValue slotReference && (IsReferenceSlot || source.IsHeapObjectReference())) {
            WriteReference(slotReference, source);
            return;
        }
        var destination = storage.UnwrapDebugValue();

        // Value-typed storage. Value-type locations may be surfaced as an ICorDebugReferenceValue (e.g. enum locals),
        // so the discriminating signal is the dereferenced destination being a value-type generic, not whether the storage itself is a reference
        if (destination is ICorDebugGenericValue destinationGeneric && destinationGeneric.GetElementType().IsValueType()) {
            if (source.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric) {
                if (destinationGeneric.GetSize() != sourceGeneric.GetSize())
                    throw new InvalidOperationException("CIL value sizes do not match");
                destinationGeneric.SetValueFromBytes(sourceGeneric.GetValueAsBytes());
                return;
            }
            if (source.Value != null) {
                destinationGeneric.SetValueFromBytes(CilValueEncoding.GetBytes(source.Value, destinationGeneric.GetElementType(), destinationGeneric.GetSize()));
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
            WriteReference(destinationReference, source);
            return;
        }

        throw new NotSupportedException("The CIL value cannot be stored in this debuggee location");
    }
    private static void WriteReference(ICorDebugReferenceValue destinationReference, CilValue source) {
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

    private ICorDebugValue GetStorageValue() {
        if (Value is ICorDebugReferenceValue byRef && byRef.GetElementType() == CorElementType.BYREF)
            return byRef.Dereference();
        return Value;
    }
}

// The address of another location: the 'this' of a struct evaluated outside a frame. Like a by-reference frame slot it
// yields the location it points to, which the IL reads and writes through ldobj/stobj, and a store goes to that location
internal class ByRefLocation : ICilLocation {
    private readonly ICilLocation target;

    public ByRefLocation(ICilLocation target) {
        this.target = target;
    }

    public CilValue Read() {
        return CilValue.FromLocation(target);
    }
    public void Write(CilValue value) {
        target.Write(value);
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

// Encodes the interpreter's host values into the bytes of a debuggee value. Integers wrap the way CIL does: an
// unsigned slot is held as its signed twin on the stack ('uint.MaxValue' is the int -1), so the bits are
// reinterpreted rather than range checked, and a comparison result (an int) stores into a bool
internal static class CilValueEncoding {
    public static byte[] GetBytes(object value, int size) {
        var bits = GetIntegerBits(value);
        return size switch {
            1 => [unchecked((byte)bits)],
            2 => BitConverter.GetBytes(unchecked((short)bits)),
            4 => BitConverter.GetBytes(unchecked((int)bits)),
            8 => BitConverter.GetBytes(bits),
            _ => throw new NotSupportedException($"Cannot encode a primitive CIL value into a {size}-byte value type")
        };
    }
    // 'size' is the debuggee's size of the value, which for a native integer depends on the debuggee, not the debugger
    public static byte[] GetBytes(object value, CorElementType targetType, int size) {
        return targetType switch {
            CorElementType.BOOLEAN => [GetIntegerBits(value) != 0 ? (byte)1 : (byte)0],
            CorElementType.R4 => BitConverter.GetBytes(Convert.ToSingle(value)),
            CorElementType.R8 => BitConverter.GetBytes(Convert.ToDouble(value)),
            CorElementType.CHAR or CorElementType.I1 or CorElementType.U1 or CorElementType.I2 or CorElementType.U2
                or CorElementType.I4 or CorElementType.U4 or CorElementType.I8 or CorElementType.U8
                or CorElementType.I or CorElementType.U or CorElementType.VALUETYPE => GetBytes(value, size),
            _ => throw new NotSupportedException($"Cannot encode a primitive CIL value as '{targetType}'")
        };
    }
    public static byte[] GetBytes(object value, CorElementType targetType) {
        return GetBytes(value, targetType, GetSize(targetType));
    }
    public static int GetSize(CorElementType elementType) {
        return elementType switch {
            CorElementType.BOOLEAN or CorElementType.I1 or CorElementType.U1 => 1,
            CorElementType.CHAR or CorElementType.I2 or CorElementType.U2 => 2,
            CorElementType.I4 or CorElementType.U4 or CorElementType.R4 => 4,
            CorElementType.I8 or CorElementType.U8 or CorElementType.R8 => 8,
            _ => throw new NotSupportedException($"The size of a '{elementType}' value is not fixed")
        };
    }
    // The host primitive 'elementType' bytes hold at 'offset', null for an element type that is not a primitive
    public static object? Decode(byte[] data, int offset, CorElementType elementType) {
        var bytes = data.AsSpan(offset);
        return elementType switch {
            CorElementType.BOOLEAN => data[offset] != 0,
            CorElementType.CHAR => BitConverter.ToChar(bytes),
            CorElementType.I1 => unchecked((sbyte)data[offset]),
            CorElementType.U1 => data[offset],
            CorElementType.I2 => BitConverter.ToInt16(bytes),
            CorElementType.U2 => BitConverter.ToUInt16(bytes),
            CorElementType.I4 => BitConverter.ToInt32(bytes),
            CorElementType.U4 => BitConverter.ToUInt32(bytes),
            CorElementType.I8 => BitConverter.ToInt64(bytes),
            CorElementType.U8 => BitConverter.ToUInt64(bytes),
            CorElementType.R4 => BitConverter.ToSingle(bytes),
            CorElementType.R8 => BitConverter.ToDouble(bytes),
            CorElementType.I => bytes.Length == 8 ? BitConverter.ToInt64(bytes) : BitConverter.ToInt32(bytes),
            CorElementType.U => bytes.Length == 8 ? BitConverter.ToUInt64(bytes) : BitConverter.ToUInt32(bytes),
            _ => null
        };
    }
    // The element type of a host primitive
    public static CorElementType GetElementType(object value) {
        return value switch {
            bool => CorElementType.BOOLEAN,
            char => CorElementType.CHAR,
            sbyte => CorElementType.I1,
            byte => CorElementType.U1,
            short => CorElementType.I2,
            ushort => CorElementType.U2,
            int => CorElementType.I4,
            uint => CorElementType.U4,
            long => CorElementType.I8,
            ulong => CorElementType.U8,
            float => CorElementType.R4,
            double => CorElementType.R8,
            _ => throw new NotSupportedException($"Value '{value.GetType().Name}' is not a primitive CIL value")
        };
    }
    private static long GetIntegerBits(object value) {
        return value switch {
            bool it => it ? 1 : 0,
            char it => it,
            sbyte it => it,
            byte it => it,
            short it => it,
            ushort it => it,
            int it => it,
            uint it => it,
            long it => it,
            ulong it => unchecked((long)it),
            float it => unchecked((long)it),
            double it => unchecked((long)it),
            _ => throw new NotSupportedException($"A '{value.GetType().Name}' value cannot be encoded as an integer")
        };
    }
}
