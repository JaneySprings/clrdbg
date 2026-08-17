namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-il-map-structure
public struct CorIlMap {
    public uint oldOffset;
    public uint newOffset;
    public int fAccurate;
}