namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corgenericparamattr-enumeration
public enum CorGenericParamAttr {
    gpVarianceMask = 3,
    gpNonVariant = 0,
    gpCovariant = 1,
    gpContravariant = 2,
    gpSpecialConstraintMask = 60,
    gpNoSpecialConstraint = gpNonVariant,
    gpReferenceTypeConstraint = 4,
    gpNotNullableValueTypeConstraint = 8,
    gpDefaultConstructorConstraint = 16,
    gpAllowByRefLike = 32
}