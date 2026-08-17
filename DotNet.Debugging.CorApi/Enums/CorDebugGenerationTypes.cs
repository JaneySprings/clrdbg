namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebuggenerationtypes-enumeration
public enum CorDebugGenerationTypes {
    CorDebug_Gen0 = 0,
    CorDebug_Gen1 = 1,
    CorDebug_Gen2 = 2,
    CorDebug_LOH = 3,
    CorDebug_POH = 4,
    CorDebug_NonGC = int.MaxValue
}