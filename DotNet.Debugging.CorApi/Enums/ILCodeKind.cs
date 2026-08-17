namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/ilcodekind-enumeration
public enum ILCodeKind {
    ILCODE_ORIGINAL_IL = 1,
    ILCODE_REJIT_IL
}