namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corgenericparamattr-enumeration
public enum CorGenericParamAttr {
    gpVarianceMask = 0x0003,
    gpNonVariant = 0x0000,
    gpCovariant = 0x0001,
    gpContravariant = 0x0002,
    gpSpecialConstraintMask = 0x003C,
    gpNoSpecialConstraint = gpNonVariant,
    gpReferenceTypeConstraint = 0x0004,
    gpNotNullableValueTypeConstraint = 0x0008,
    gpDefaultConstructorConstraint = 0x0010,
    gpAllowByRefLike = 0x0020
}