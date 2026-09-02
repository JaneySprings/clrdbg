namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corthreadsafetyoptions-enumeration
public enum CorThreadSafetyOptions {
    MDThreadSafetyDefault = 0x00000000,
    MDThreadSafetyOff = MDThreadSafetyDefault,
    MDThreadSafetyOn = 0x00000001
}