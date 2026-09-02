namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corvalidatormoduletype-enumeration
public enum CorValidatorModuleType {
    ValidatorModuleTypeInvalid = 0x0,
    ValidatorModuleTypeMin = 0x00000001,
    ValidatorModuleTypePE = ValidatorModuleTypeMin,
    ValidatorModuleTypeObj = 0x00000002,
    ValidatorModuleTypeEnc = 0x00000003,
    ValidatorModuleTypeIncr = 0x00000004,
    ValidatorModuleTypeMax = ValidatorModuleTypeIncr
}