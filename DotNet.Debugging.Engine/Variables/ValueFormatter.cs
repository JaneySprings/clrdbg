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
    // The display text, or an interpolated string template when 'RequiresDebuggerDisplay' is set
    public string Value { get; }
    public bool RequiresDebuggerDisplay { get; }
    public string? DebuggerProxyTypeName { get; }

    public FormattedValue(string typeName, string value, bool requiresDebuggerDisplay = false, string? debuggerProxyTypeName = null) {
        TypeName = typeName;
        Value = value;
        RequiresDebuggerDisplay = requiresDebuggerDisplay;
        DebuggerProxyTypeName = debuggerProxyTypeName;
    }
}

// Formats debuggee values the way the C# debugger shows them. Values whose display needs code to run in the
// debuggee (DebuggerDisplay, ToString overrides) come back as a template for the expression evaluator
internal static class ValueFormatter {
    private const CorFieldAttr EnumMemberAttributes = CorFieldAttr.fdPublic | CorFieldAttr.fdStatic | CorFieldAttr.fdLiteral | CorFieldAttr.fdHasDefault;

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

    // The underlying value of a Nullable<T>, null when it has no value
    public static ICorDebugValue? GetNullableValue(ICorDebugObjectValue objectValue) {
        var corClass = objectValue.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        var hasValueField = metadataImport.FindField(corClass.GetToken(), "hasValue", 0, 0);
        var valueField = metadataImport.FindField(corClass.GetToken(), "value", 0, 0);

        var hasValue = Format(objectValue.GetFieldValue(corClass, hasValueField), false);
        if (hasValue.Value == "false")
            return null;
        return objectValue.GetFieldValue(corClass, valueField);
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
    private static FormattedValue FormatArray(ICorDebugArrayValue arrayValue) {
        var typeName = TypeNameFormatter.GetTypeName(arrayValue.GetExactType());
        var elementTypeName = typeName.Substring(0, typeName.LastIndexOf('['));
        var dimensions = arrayValue.GetDimensions(arrayValue.GetRank());
        return new FormattedValue(typeName, $"{{{elementTypeName}[{string.Join(", ", dimensions)}]}}");
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

        if (GetBaseTypeName(exactType) == "System.Enum") {
            var valueField = metadataImport.FindField(classToken, "value__", 0, 0);
            var numericValue = Format(objectValue.GetFieldValue(corClass, valueField), escapeStrings).Value;
            return new FormattedValue(typeName, FormatEnum(metadataImport, classToken, numericValue));
        }
        if (typeName.EndsWith('?')) {
            var underlyingValue = GetNullableValue(objectValue);
            if (underlyingValue == null)
                return new FormattedValue(typeName, "null");
            return new FormattedValue(typeName, Format(underlyingValue, escapeStrings).Value);
        }

        string? proxyTypeName = null;
        if (metadataImport.TryGetCustomAttributeByName(classToken, AttributeNames.DebuggerTypeProxy, out var proxyData, out var proxySize) == Cor.S_OK)
            proxyTypeName = CustomAttributeReader.ReadStringArgument(proxyData, proxySize);

        if (metadataImport.TryGetCustomAttributeByName(classToken, AttributeNames.DebuggerDisplay, out var displayData, out var displaySize) == Cor.S_OK) {
            var display = CustomAttributeReader.ReadStringArgument(displayData, displaySize) ?? string.Empty;
            if (typeName.StartsWith("<>f__AnonymousType", StringComparison.Ordinal)) {
                // An anonymous type's display is '\{ Id = {Id}, Name = {Name} }' - the escaped braces
                // have to become '{{' and '}}' to be a valid interpolated string
                display = string.Concat("{{", display.AsSpan(2, display.Length - 3), "}}");
            }
            // The 'Name' part of the attribute is shown as a prefix of the value rather than replacing the variable name
            var displayName = CustomAttributeReader.ReadNamedStringArgument(displayData, displaySize, "Name");
            if (displayName != null)
                display = $"{displayName} = {display}";
            return new FormattedValue(typeName, display, true, proxyTypeName);
        }
        if (exactType.IsExceptionType())
            return new FormattedValue(typeName, "{ToString()}", true, proxyTypeName);
        if (typeName == "decimal")
            return new FormattedValue(typeName, FormatDecimal(objectValue));
        if (OverridesToString(exactType))
            return new FormattedValue(typeName, "{ToString()}", true, proxyTypeName);

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

    // Whether the type or any base type up to System.Object/System.ValueType declares a parameterless ToString override
    private static bool OverridesToString(ICorDebugType type) {
        var current = type;
        while (current != null) {
            var corClass = current.GetClass();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            var typeName = metadataImport.GetTypeDefProps(corClass.GetToken()).szTypeDef;
            if (typeName == "System.Object" || typeName == "System.ValueType")
                return false;

            foreach (var methodToken in metadataImport.EnumMethodsWithName(corClass.GetToken(), "ToString")) {
                var methodProps = metadataImport.GetMethodProps(methodToken);
                var attributes = methodProps.pdwAttr;
                var parameterCount = Marshal.ReadByte(methodProps.ppvSigBlob, 1);
                if (!attributes.IsMdStatic() && attributes.IsMdVirtual() && !attributes.IsMdNewSlot() && parameterCount == 0)
                    return true;
            }
            current = current.GetBase();
        }
        return false;
    }
    private static string? GetBaseTypeName(ICorDebugType type) {
        var baseType = type.GetBase();
        if (baseType == null)
            return null;
        var corClass = baseType.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        return metadataImport.GetTypeDefProps(corClass.GetToken()).szTypeDef;
    }

    private static string FormatChar(char value) {
        return $"{(int)value} {SymbolDisplay.FormatLiteral(value, quote: true)}";
    }
    private static string FormatNumber(IFormattable value) {
        return value.ToString(null, CultureInfo.InvariantCulture);
    }
}
