using System.Globalization;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;

namespace DotNet.Debugging.Engine.Variables;

// Assigns new values typed by the user. Only primitive values and 'null' for references are supported
internal static class VariableWriter {
    public static void Write(ICorDebugValue target, string text) {
        var expression = text.Trim();
        if (expression == "null") {
            if (target is not ICorDebugReferenceValue referenceValue)
                throw new InvalidOperationException("Cannot assign 'null' to a value type");
            var result = referenceValue.TrySetValue(default);
            if (result != Cor.S_OK)
                throw new InvalidOperationException($"Cannot assign 'null' to the variable: 0x{result:X8}");
            return;
        }

        var genericValue = target as ICorDebugGenericValue ?? target.UnwrapDebugValue() as ICorDebugGenericValue;
        if (genericValue == null)
            throw new InvalidOperationException("Only primitive values are supported");

        var size = genericValue.GetSize();
        var bytes = Parse(genericValue.GetElementType(), size, expression);
        if (bytes.Length != size)
            throw new InvalidOperationException($"Value size mismatch for type '{genericValue.GetElementType()}'");
        genericValue.SetValueFromBytes(bytes);
    }

    // 'size' is the debuggee's size of the value: a native integer is as wide as the debuggee, not the debugger
    private static byte[] Parse(CorElementType elementType, int size, string text) {
        var culture = CultureInfo.InvariantCulture;
        try {
            return elementType switch {
                CorElementType.BOOLEAN => [(byte)(bool.Parse(text) ? 1 : 0)],
                CorElementType.CHAR => BitConverter.GetBytes(ParseChar(text)),
                CorElementType.I1 => [unchecked((byte)sbyte.Parse(text, culture))],
                CorElementType.U1 => [byte.Parse(text, culture)],
                CorElementType.I2 => BitConverter.GetBytes(short.Parse(text, culture)),
                CorElementType.U2 => BitConverter.GetBytes(ushort.Parse(text, culture)),
                CorElementType.I4 => BitConverter.GetBytes(int.Parse(text, culture)),
                CorElementType.U4 => BitConverter.GetBytes(uint.Parse(text, culture)),
                CorElementType.I8 => BitConverter.GetBytes(long.Parse(text, culture)),
                CorElementType.U8 => BitConverter.GetBytes(ulong.Parse(text, culture)),
                CorElementType.R4 => BitConverter.GetBytes(float.Parse(text, culture)),
                CorElementType.R8 => BitConverter.GetBytes(double.Parse(text, culture)),
                CorElementType.I => size == 4 ? BitConverter.GetBytes(int.Parse(text, culture)) : BitConverter.GetBytes(long.Parse(text, culture)),
                CorElementType.U => size == 4 ? BitConverter.GetBytes(uint.Parse(text, culture)) : BitConverter.GetBytes(ulong.Parse(text, culture)),
                _ => throw new InvalidOperationException($"Setting values of type '{elementType}' is not supported")
            };
        }
        catch (Exception ex) when (ex is not InvalidOperationException) {
            throw new InvalidOperationException($"Cannot parse value '{text}': {ex.Message}");
        }
    }
    private static char ParseChar(string text) {
        if (text.Length == 3 && text[0] == '\'' && text[2] == '\'')
            return text[1];
        if (text.Length == 1)
            return text[0];
        return (char)ushort.Parse(text, CultureInfo.InvariantCulture);
    }
}
