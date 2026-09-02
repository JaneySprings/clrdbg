namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugmappingresult-enumeration
public enum CorDebugMappingResult {
    MAPPING_PROLOG = 0x1,
    MAPPING_EPILOG = 0x2,
    MAPPING_NO_INFO = 0x4,
    MAPPING_UNMAPPED_ADDRESS = 0x8,
    MAPPING_EXACT = 0x10,
    MAPPING_APPROXIMATE = 0x20
}