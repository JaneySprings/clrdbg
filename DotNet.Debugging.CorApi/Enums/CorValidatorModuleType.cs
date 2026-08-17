namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corvalidatormoduletype-enumeration
public enum CorValidatorModuleType {
    ValidatorModuleTypeInvalid = 0,
    ValidatorModuleTypeMin = 1,
    ValidatorModuleTypePE = ValidatorModuleTypeMin,
    ValidatorModuleTypeObj = 2,
    ValidatorModuleTypeEnc = 3,
    ValidatorModuleTypeIncr = 4,
    ValidatorModuleTypeMax = ValidatorModuleTypeIncr
}