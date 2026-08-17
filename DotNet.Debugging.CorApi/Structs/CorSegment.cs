namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-segment-structure
public struct CorSegment {
    public CordbAddress start;
    public CordbAddress end;
    public CorDebugGenerationTypes type;
    public uint heap;
}