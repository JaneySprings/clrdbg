namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugiltonativemappingtypes-enumeration
public enum CorDebugIlToNativeMappingTypes {
    NO_MAPPING = -1,
    PROLOG = -2,
    EPILOG = -3
}