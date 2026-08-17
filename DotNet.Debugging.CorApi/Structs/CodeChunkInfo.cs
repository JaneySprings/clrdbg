namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/codechunkinfo-structure
public struct CodeChunkInfo {
    public CordbAddress startAddr;
    public uint length;
}