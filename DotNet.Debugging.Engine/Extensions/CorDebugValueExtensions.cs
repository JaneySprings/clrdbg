using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Evaluation;
using DotNet.Debugging.Evaluation;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugValueExtensions {
    // Follows a non-null reference and unboxes a boxed value
    public static ICorDebugValue UnwrapDebugValue(this ICorDebugValue value) {
        var result = value;
        if (result is ICorDebugReferenceValue referenceValue && !referenceValue.IsNull())
            result = referenceValue.Dereference();
        if (result is ICorDebugBoxValue boxValue)
            result = boxValue.GetObject();
        return result;
    }
    public static ICorDebugObjectValue UnwrapDebugValueToObject(this ICorDebugValue value) {
        if (value.UnwrapDebugValue() is ICorDebugObjectValue objectValue)
            return objectValue;
        throw new InvalidOperationException("The value is not an object");
    }

    // The runtime copies the value in and out of the pinned array, no native buffer is needed in between
    public static unsafe byte[] GetValueAsBytes(this ICorDebugGenericValue value) {
        var result = new byte[value.GetSize()];
        fixed (byte* buffer = result)
            value.GetValue((nint)buffer);
        return result;
    }
    public static unsafe void SetValueFromBytes(this ICorDebugGenericValue value, byte[] bytes) {
        fixed (byte* buffer = bytes)
            value.SetValue((nint)buffer);
    }
    // A primitive value as the host type it is ('int', 'double'), null for any other value
    public static object? ReadPrimitive(this ICorDebugValue value) {
        if (value.UnwrapDebugValue() is not ICorDebugGenericValue generic)
            return null;
        return generic.ReadPrimitive(generic.GetElementType());
    }
    // The bytes of the value decoded as 'elementType': the object of a boxed primitive reports VALUETYPE, its class says which
    public static object? ReadPrimitive(this ICorDebugGenericValue generic, CorElementType elementType) {
        return CilValueEncoding.Decode(generic.GetValueAsBytes(), 0, elementType);
    }

    // The value of an instance field declared on the object's own class, null when the class has no such field
    public static ICorDebugValue? FindFieldValue(this ICorDebugObjectValue objectValue, string fieldName) {
        var corClass = objectValue.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        var fieldDef = metadataImport.EnumFieldsWithName(corClass.GetToken(), fieldName).SingleOrDefault();
        if (fieldDef.IsNil)
            return null;
        return objectValue.GetFieldValue(corClass, fieldDef);
    }
    // Whether the field of that name, on the object's type or a base type, is a constant
    public static bool IsLiteralField(this ICorDebugObjectValue objectValue, string fieldName) {
        for (var type = objectValue.GetExactType(); type != null; type = type.GetBaseType()) {
            var corClass = type.GetClass();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            var fieldDef = metadataImport.EnumFieldsWithName(corClass.GetToken(), fieldName).SingleOrDefault();
            if (!fieldDef.IsNil)
                return fieldDef.IsLiteral(metadataImport);
        }
        return false;
    }
    // Reads an instance, static or literal field declared on the object's type or one of its base types
    public static ICorDebugValue? GetFieldValueByName(this ICorDebugObjectValue objectValue, ICorDebugILFrame frame, string fieldName) {
        var type = objectValue.GetExactType();
        while (type != null) {
            var corClass = type.GetClass();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            var fieldDef = metadataImport.EnumFieldsWithName(corClass.GetToken(), fieldName).SingleOrDefault();
            if (fieldDef.IsNil) {
                type = type.GetBaseType();
                continue;
            }

            if (fieldDef.IsLiteral(metadataImport))
                return CreateLiteralValue(metadataImport, fieldDef, frame);
            if (fieldDef.IsStatic(metadataImport))
                return type.GetStaticFieldValue(fieldDef, frame);
            return objectValue.GetFieldValue(corClass, fieldDef);
        }
        return null;
    }
    // The underlying value of a Nullable<T>, null when it has no value
    public static ICorDebugValue? GetNullableValue(this ICorDebugObjectValue objectValue) {
        var corClass = objectValue.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        var hasValueField = metadataImport.FindField(corClass.GetToken(), "hasValue", 0, 0);
        var valueField = metadataImport.FindField(corClass.GetToken(), "value", 0, 0);
        if (objectValue.GetFieldValue(corClass, hasValueField).UnwrapDebugValue() is not ICorDebugGenericValue hasValue || hasValue.GetValueAsBytes()[0] == 0)
            return null;
        return objectValue.GetFieldValue(corClass, valueField);
    }
    // The integer of an enum, read through its 'value__' field: the field's primitive type carries the sign of the
    // underlying type (an 'sbyte' member -1 must not read as 255)
    public static bool TryReadEnumValue(this ICorDebugObjectValue objectValue, out long value) {
        value = 0;
        var valueField = objectValue.FindFieldValue("value__");
        if (valueField == null)
            return false;
        switch (valueField.ReadPrimitive()) {
            case sbyte it: value = it; return true;
            case byte it: value = it; return true;
            case short it: value = it; return true;
            case ushort it: value = it; return true;
            case int it: value = it; return true;
            case uint it: value = it; return true;
            case long it: value = it; return true;
            case ulong it: value = unchecked((long)it); return true;
            case char it: value = it; return true;
        }
        return false;
    }
    // The user's 'this' captured by a closure or state machine, following the chain of enclosing closures
    public static ICorDebugValue? GetHoistedThis(this ICorDebugValue generatedInstance) {
        // An enclosing closure not created yet (its captured variables are not in scope) leaves the field null
        if (generatedInstance.UnwrapDebugValue() is not ICorDebugObjectValue objectValue)
            return null;
        var corClass = objectValue.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        foreach (var field in metadataImport.EnumFields(corClass.GetToken())) {
            var kind = GeneratedNames.GetKind(metadataImport.GetFieldProps(field).szField);
            if (kind == GeneratedNameKind.ThisProxyField)
                return objectValue.GetFieldValue(corClass, field);
            if (kind == GeneratedNameKind.DisplayClassLocalOrField)
                return objectValue.GetFieldValue(corClass, field).GetHoistedThis();
        }
        return null;
    }

    private static ICorDebugGenericValue CreateLiteralValue(IMetaDataImport metadataImport, FieldDefToken fieldDef, ICorDebugILFrame frame) {
        var fieldProps = metadataImport.GetFieldProps(fieldDef);
        var eval = frame.GetChain().GetThread().CreateEval();
        if (eval.CreateValue(fieldProps.pdwCPlusTypeFlag, null) is not ICorDebugGenericValue value)
            throw new InvalidOperationException("Expected a generic value for the literal field");
        value.SetValue(fieldProps.ppValue);
        return value;
    }
}
