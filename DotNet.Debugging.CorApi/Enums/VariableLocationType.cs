namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/variablelocationtype-enumeration
public enum VariableLocationType {
    VLT_REGISTER,
    VLT_REGISTER_RELATIVE,
    VLT_INVALID
}