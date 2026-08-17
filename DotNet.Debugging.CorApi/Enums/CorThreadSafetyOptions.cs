namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corthreadsafetyoptions-enumeration
public enum CorThreadSafetyOptions {
    MDThreadSafetyDefault = 0,
    MDThreadSafetyOff = MDThreadSafetyDefault,
    MDThreadSafetyOn = 1
}