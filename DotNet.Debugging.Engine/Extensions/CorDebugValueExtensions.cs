using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

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

    public static bool IsExceptionType(this ICorDebugType type) {
        var current = type;
        while (current != null) {
            var corClass = current.GetClass();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            if (metadataImport.GetTypeDefProps(corClass.GetToken()).szTypeDef == "System.Exception")
                return true;
            current = current.GetBaseType();
        }
        return false;
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
