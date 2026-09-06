using System.Reflection.Metadata;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Extensions;

internal static class EnumExtensions {
    // 'protected internal' is internal at least, 'private protected' is protected at most
    public static VariableVisibility ToVisibility(this CorFieldAttr attributes) {
        return (attributes & CorFieldAttr.fdFieldAccessMask) switch {
            CorFieldAttr.fdPublic => VariableVisibility.Public,
            CorFieldAttr.fdFamily or CorFieldAttr.fdFamANDAssem => VariableVisibility.Protected,
            CorFieldAttr.fdAssembly or CorFieldAttr.fdFamORAssem => VariableVisibility.Internal,
            _ => VariableVisibility.Private
        };
    }
    public static VariableVisibility ToVisibility(this CorMethodAttr attributes) {
        return (attributes & CorMethodAttr.mdMemberAccessMask) switch {
            CorMethodAttr.mdPublic => VariableVisibility.Public,
            CorMethodAttr.mdFamily or CorMethodAttr.mdFamANDAssem => VariableVisibility.Protected,
            CorMethodAttr.mdAssem or CorMethodAttr.mdFamORAssem => VariableVisibility.Internal,
            _ => VariableVisibility.Private
        };
    }

    // A struct or one of the primitives stored by value
    public static bool IsValueType(this CorElementType elementType) {
        return elementType is CorElementType.VALUETYPE or CorElementType.BOOLEAN or CorElementType.CHAR
            or CorElementType.I1 or CorElementType.U1 or CorElementType.I2 or CorElementType.U2
            or CorElementType.I4 or CorElementType.U4 or CorElementType.I8 or CorElementType.U8
            or CorElementType.R4 or CorElementType.R8 or CorElementType.I or CorElementType.U;
    }
    public static PrimitiveTypeCode? ToPrimitiveTypeCode(this CorElementType elementType) {
        return elementType switch {
            CorElementType.BOOLEAN => PrimitiveTypeCode.Boolean,
            CorElementType.CHAR => PrimitiveTypeCode.Char,
            CorElementType.I1 => PrimitiveTypeCode.SByte,
            CorElementType.U1 => PrimitiveTypeCode.Byte,
            CorElementType.I2 => PrimitiveTypeCode.Int16,
            CorElementType.U2 => PrimitiveTypeCode.UInt16,
            CorElementType.I4 => PrimitiveTypeCode.Int32,
            CorElementType.U4 => PrimitiveTypeCode.UInt32,
            CorElementType.I8 => PrimitiveTypeCode.Int64,
            CorElementType.U8 => PrimitiveTypeCode.UInt64,
            CorElementType.R4 => PrimitiveTypeCode.Single,
            CorElementType.R8 => PrimitiveTypeCode.Double,
            CorElementType.I => PrimitiveTypeCode.IntPtr,
            CorElementType.U => PrimitiveTypeCode.UIntPtr,
            CorElementType.STRING => PrimitiveTypeCode.String,
            CorElementType.OBJECT => PrimitiveTypeCode.Object,
            _ => null
        };
    }
    // The element type of the primitives held by value, null for 'string' and 'object'
    public static CorElementType? ToCorElementType(this PrimitiveTypeCode primitive) {
        return primitive switch {
            PrimitiveTypeCode.Boolean => CorElementType.BOOLEAN,
            PrimitiveTypeCode.Char => CorElementType.CHAR,
            PrimitiveTypeCode.SByte => CorElementType.I1,
            PrimitiveTypeCode.Byte => CorElementType.U1,
            PrimitiveTypeCode.Int16 => CorElementType.I2,
            PrimitiveTypeCode.UInt16 => CorElementType.U2,
            PrimitiveTypeCode.Int32 => CorElementType.I4,
            PrimitiveTypeCode.UInt32 => CorElementType.U4,
            PrimitiveTypeCode.Int64 => CorElementType.I8,
            PrimitiveTypeCode.UInt64 => CorElementType.U8,
            PrimitiveTypeCode.Single => CorElementType.R4,
            PrimitiveTypeCode.Double => CorElementType.R8,
            PrimitiveTypeCode.IntPtr => CorElementType.I,
            PrimitiveTypeCode.UIntPtr => CorElementType.U,
            _ => null
        };
    }
}
