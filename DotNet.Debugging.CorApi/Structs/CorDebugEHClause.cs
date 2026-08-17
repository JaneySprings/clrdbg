namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugehclause-structure
public struct CorDebugEHClause {
    public uint Flags;
    public uint TryOffset;
    public uint TryLength;
    public uint HandlerOffset;
    public uint HandlerLength;
    public uint ClassToken;
    public uint FilterOffset;
}