namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugmappingresult-enumeration
public enum CorDebugMappingResult {
    MAPPING_PROLOG = 1,
    MAPPING_EPILOG = 2,
    MAPPING_NO_INFO = 4,
    MAPPING_UNMAPPED_ADDRESS = 8,
    MAPPING_EXACT = 0x10,
    MAPPING_APPROXIMATE = 0x20
}