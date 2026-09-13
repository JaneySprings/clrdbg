using System.Globalization;
using System.Runtime.InteropServices;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Metadata;
using Microsoft.CodeAnalysis.CSharp;

namespace DotNet.Debugging.Engine.Variables;

internal class FormattedValue {
    public string TypeName { get; }
    // The display text, or a DebuggerDisplay string (see DebuggerDisplayTemplate) when 'RequiresDebuggerDisplay' is set
    public string Value { get; }
    public bool RequiresDebuggerDisplay { get; }
    // The attribute's 'Name' and 'Type' strings, shown in place of a member's name and of the type name
    public string? NameTemplate { get; }
    public string? TypeTemplate { get; }
    public string? DebuggerProxyTypeName { get; }

    public FormattedValue(string typeName, string value, bool requiresDebuggerDisplay = false, string? debuggerProxyTypeName = null, string? nameTemplate = null, string? typeTemplate = null) {
        TypeName = typeName;
        Value = value;
        RequiresDebuggerDisplay = requiresDebuggerDisplay;
        DebuggerProxyTypeName = debuggerProxyTypeName;
        NameTemplate = nameTemplate;
        TypeTemplate = typeTemplate;
    }
}

// Formats debuggee values the way the C# debugger shows them. Values whose display needs code to run in the
// debuggee (DebuggerDisplay, ToString overrides) come back as a template for the expression evaluator
internal static class ValueFormatter {
    private const CorFieldAttr EnumMemberAttributes = CorFieldAttr.fdPublic | CorFieldAttr.fdStatic | CorFieldAttr.fdLiteral | CorFieldAttr.fdHasDefault;
    // A ToString result is shown in braces ('{Text}') and never quoted, written as a display string would write it
    private const string ToStringTemplate = "\\{{ToString(),nq}\\}";

    public static FormattedValue Format(ICorDebugValue value, bool escapeStrings) {
        switch (value) {
            case ICorDebugBoxValue boxValue:
                return Format(boxValue.GetObject(), escapeStrings);
            case ICorDebugArrayValue arrayValue:
                return FormatArray(arrayValue);
            case ICorDebugStringValue stringValue:
                return FormatString(stringValue, escapeStrings);
            case ICorDebugObjectValue objectValue:
                return FormatObject(objectValue, escapeStrings);
            case ICorDebugReferenceValue referenceValue:
                return FormatReference(referenceValue, escapeStrings);
            case ICorDebugGenericValue genericValue:
                return FormatPrimitive(genericValue);
            default:
                throw new ArgumentOutOfRangeException(nameof(value), "Unknown value kind");
        }
    }

    public static string FormatLiteral(nint data, int length, CorElementType elementType) {
        if (data == IntPtr.Zero)
            throw new ArgumentNullException(nameof(data));

        return elementType switch {
            CorElementType.BOOLEAN => Marshal.ReadByte(data) != 0 ? "true" : "false",
            CorElementType.CHAR => FormatChar((char)Marshal.ReadInt16(data)),
            CorElementType.I1 => FormatNumber((sbyte)Marshal.ReadByte(data)),
            CorElementType.U1 => FormatNumber(Marshal.ReadByte(data)),
            CorElementType.I2 => FormatNumber(Marshal.ReadInt16(data)),
            CorElementType.U2 => FormatNumber((ushort)Marshal.ReadInt16(data)),
            CorElementType.I4 => FormatNumber(Marshal.ReadInt32(data)),
            CorElementType.U4 => FormatNumber((uint)Marshal.ReadInt32(data)),
            CorElementType.I8 => FormatNumber(Marshal.ReadInt64(data)),
            CorElementType.U8 => FormatNumber((ulong)Marshal.ReadInt64(data)),
            CorElementType.R4 => FormatNumber(BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data))),
            CorElementType.R8 => FormatNumber(BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data))),
            CorElementType.STRING => SymbolDisplay.FormatLiteral(Marshal.PtrToStringUni(data, length), quote: true),
            CorElementType.CLASS => "null",
            _ => throw new NotSupportedException($"Literals of type '{elementType}' are not supported")
        };
    }
    // The numeric value of an enum member literal
    public static ulong ReadLiteralNumber(nint data, CorElementType elementType) {
        return elementType switch {
            CorElementType.I1 => unchecked((ulong)(sbyte)Marshal.ReadByte(data)),
            CorElementType.U1 => Marshal.ReadByte(data),
            CorElementType.I2 => unchecked((ulong)Marshal.ReadInt16(data)),
            CorElementType.U2 => (ushort)Marshal.ReadInt16(data),
            CorElementType.I4 => unchecked((ulong)Marshal.ReadInt32(data)),
            CorElementType.U4 => (uint)Marshal.ReadInt32(data),
            CorElementType.I8 => unchecked((ulong)Marshal.ReadInt64(data)),
            CorElementType.U8 => (ulong)Marshal.ReadInt64(data),
            _ => throw new NotSupportedException($"Enum literals of type '{elementType}' are not supported")
        };
    }

    private static FormattedValue FormatString(ICorDebugStringValue stringValue, bool escapeStrings) {
        var text = stringValue.GetString();
        if (escapeStrings)
            text = SymbolDisplay.FormatLiteral(text, quote: true);
        return new FormattedValue("string", text);
    }
    // '{int[3]}', '{int[2, 3]}', '{int[3][]}' for a jagged array: the lengths go into the array's own brackets, the
    // first top-level ones of the type name, the element type's brackets stay behind them
    private static FormattedValue FormatArray(ICorDebugArrayValue arrayValue) {
        var typeName = TypeNameFormatter.GetTypeName(arrayValue.GetExactType());
        var dimensions = arrayValue.GetDimensions(arrayValue.GetRank());
        var depth = 0;
        for (var i = 0; i < typeName.Length; i++) {
            if (typeName[i] == '<')
                depth++;
            else if (typeName[i] == '>')
                depth--;
            else if (typeName[i] == '[' && depth == 0)
                return new FormattedValue(typeName, $"{{{typeName.Substring(0, i)}[{string.Join(", ", dimensions)}]{typeName.Substring(typeName.IndexOf(']', i) + 1)}}}");
        }
        return new FormattedValue(typeName, $"{{{typeName}}}");
    }
    private static FormattedValue FormatReference(ICorDebugReferenceValue referenceValue, bool escapeStrings) {
        if (referenceValue.IsNull())
            return new FormattedValue(TypeNameFormatter.GetTypeName(referenceValue.GetExactType()), "null");
        return Format(referenceValue.Dereference(), escapeStrings);
    }
    private static FormattedValue FormatObject(ICorDebugObjectValue objectValue, bool escapeStrings) {
        var corClass = objectValue.GetClass();
        var classToken = corClass.GetToken();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        var exactType = objectValue.GetExactType();
        var typeName = TypeNameFormatter.GetTypeName(exactType);

        if (exactType.IsEnumType()) {
            var valueField = metadataImport.FindField(classToken, "value__", 0, 0);
            var numericValue = Format(objectValue.GetFieldValue(corClass, valueField), escapeStrings).Value;
            return new FormattedValue(typeName, FormatEnum(metadataImport, classToken, numericValue));
        }
        if (typeName.EndsWith('?')) {
            var underlyingValue = objectValue.GetNullableValue();
            if (underlyingValue == null)
                return new FormattedValue(typeName, "null");
            // The underlying value's display template and proxy carry over, they run against that value
            var underlying = Format(underlyingValue, escapeStrings);
            return new FormattedValue(typeName, underlying.Value, underlying.RequiresDebuggerDisplay, underlying.DebuggerProxyTypeName, underlying.NameTemplate, underlying.TypeTemplate);
        }
        // A boxed primitive is shown as the primitive itself, without evaluating its ToString override
        if (TypeNameFormatter.IsPrimitiveTypeName(typeName)) {
            var valueField = metadataImport.FindField(classToken, typeName is "nint" or "nuint" ? "_value" : "m_value", 0, 0);
            return new FormattedValue(typeName, Format(objectValue.GetFieldValue(corClass, valueField), escapeStrings).Value);
        }

        // A byref-like value ('Span<T>' and friends) cannot travel through a func eval - its interior
        // pointer does not survive the trip and the debuggee can die on it. No display evaluation and
        // no debugger proxy, the members stay readable directly
        if (metadataImport.HasAttribute(classToken, AttributeNames.IsByRefLike))
            return new FormattedValue(typeName, $"{{{typeName}}}");

        string? proxyTypeName = null;
        if (TryGetInheritedAttribute(exactType, AttributeNames.DebuggerTypeProxy, out var proxyData, out var proxySize))
            proxyTypeName = CustomAttributeReader.ReadStringArgument(proxyData, proxySize);

        if (TryGetInheritedAttribute(exactType, AttributeNames.DebuggerDisplay, out var displayData, out var displaySize)) {
            var display = CustomAttributeReader.ReadStringArgument(displayData, displaySize) ?? string.Empty;
            var nameTemplate = CustomAttributeReader.ReadNamedStringArgument(displayData, displaySize, "Name");
            var typeTemplate = CustomAttributeReader.ReadNamedStringArgument(displayData, displaySize, "Type");
            return new FormattedValue(typeName, display, true, proxyTypeName, nameTemplate, typeTemplate);
        }
        if (exactType.IsExceptionType())
            return new FormattedValue(typeName, ToStringTemplate, true, proxyTypeName);
        if (typeName == "decimal")
            return new FormattedValue(typeName, FormatDecimal(objectValue));
        if (exactType.OverridesToString())
            return new FormattedValue(typeName, ToStringTemplate, true, proxyTypeName);

        return new FormattedValue(typeName, $"{{{typeName}}}", false, proxyTypeName);
    }
    private static FormattedValue FormatPrimitive(ICorDebugGenericValue genericValue) {
        var elementType = genericValue.GetElementType();
        var typeName = TypeNameFormatter.GetPrimitiveTypeName(elementType)
            ?? throw new NotSupportedException($"Values of type '{elementType}' are not supported");
        if (elementType == CorElementType.VOID)
            return new FormattedValue(typeName, "void");

        var data = genericValue.GetValueAsBytes();
        var value = elementType switch {
            CorElementType.BOOLEAN => data[0] != 0 ? "true" : "false",
            CorElementType.CHAR => FormatChar(BitConverter.ToChar(data)),
            CorElementType.I1 => FormatNumber((sbyte)data[0]),
            CorElementType.U1 => FormatNumber(data[0]),
            CorElementType.I2 => FormatNumber(BitConverter.ToInt16(data)),
            CorElementType.U2 => FormatNumber(BitConverter.ToUInt16(data)),
            CorElementType.I4 => FormatNumber(BitConverter.ToInt32(data)),
            CorElementType.U4 => FormatNumber(BitConverter.ToUInt32(data)),
            CorElementType.I8 => FormatNumber(BitConverter.ToInt64(data)),
            CorElementType.U8 => FormatNumber(BitConverter.ToUInt64(data)),
            CorElementType.R4 => FormatNumber(BitConverter.ToSingle(data)),
            CorElementType.R8 => FormatNumber(BitConverter.ToDouble(data)),
            CorElementType.I => data.Length == 4 ? FormatNumber(BitConverter.ToInt32(data)) : FormatNumber(BitConverter.ToInt64(data)),
            CorElementType.U => data.Length == 4 ? FormatNumber(BitConverter.ToUInt32(data)) : FormatNumber(BitConverter.ToUInt64(data)),
            _ => throw new NotSupportedException($"Values of type '{elementType}' are not supported")
        };
        return new FormattedValue(typeName, value);
    }

    private static string FormatDecimal(ICorDebugObjectValue objectValue) {
        // The struct layout is flags, hi, lo, mid (16 bytes) on every runtime
        if (objectValue is not ICorDebugGenericValue genericValue || genericValue.GetSize() != 16)
            return "{decimal}";
        var data = genericValue.GetValueAsBytes();
        var flags = BitConverter.ToInt32(data, 0);
        var hi = BitConverter.ToInt32(data, 4);
        var lo = BitConverter.ToInt32(data, 8);
        var mid = BitConverter.ToInt32(data, 12);
        return new decimal([lo, mid, hi, flags]).ToString(CultureInfo.InvariantCulture);
    }
    private static string FormatEnum(IMetaDataImport metadataImport, TypeDefToken enumToken, string numericValue) {
        if (!ulong.TryParse(numericValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) {
            // A negative member of a signed enum
            if (!long.TryParse(numericValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedValue))
                return numericValue;
            value = unchecked((ulong)signedValue);
        }

        var members = new List<KeyValuePair<ulong, string>>();
        foreach (var field in metadataImport.EnumFields(enumToken)) {
            var fieldProps = metadataImport.GetFieldProps(field);
            if ((fieldProps.pdwAttr & EnumMemberAttributes) != EnumMemberAttributes)
                continue;
            var memberValue = ReadLiteralNumber(fieldProps.ppValue, fieldProps.pdwCPlusTypeFlag);
            if (memberValue == value)
                return fieldProps.szField;
            members.Add(new KeyValuePair<ulong, string>(memberValue, fieldProps.szField));
        }

        if (!metadataImport.HasAttribute(enumToken, AttributeNames.Flags))
            return numericValue;

        // Decompose a [Flags] value into its members, zero is never part of a combination
        var names = new List<string>();
        var remaining = value;
        foreach (var member in members.OrderBy(it => it.Key)) {
            if (member.Key == 0 || (member.Key & remaining) != member.Key)
                continue;
            names.Add(member.Value);
            remaining &= ~member.Key;
        }
        return names.Count > 0 && remaining == 0 ? string.Join(" | ", names) : numericValue;
    }

    // The attributes are inherited: a List<T> subclass shows List<T>'s display and proxy, the way the C# debugger does
    private static bool TryGetInheritedAttribute(ICorDebugType type, string attributeName, out nint data, out uint size) {
        for (var current = type; current != null; current = current.GetBaseType()) {
            var corClass = current.GetClass();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            if (metadataImport.TryGetCustomAttributeByName(corClass.GetToken(), attributeName, out data, out size) == Cor.S_OK)
                return true;
        }
        data = 0;
        size = 0;
        return false;
    }
    private static string FormatChar(char value) {
        return $"{(int)value} {SymbolDisplay.FormatLiteral(value, quote: true)}";
    }
    private static string FormatNumber(IFormattable value) {
        return value.ToString(null, CultureInfo.InvariantCulture);
    }
}
