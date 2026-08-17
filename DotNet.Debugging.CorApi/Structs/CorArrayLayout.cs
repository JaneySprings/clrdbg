namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-array-layout-structure
public struct CorArrayLayout {
    public CorTypeId componentID;
    public CorElementType componentType;
    public uint firstElementOffset;
    public uint elementSize;
    public uint countOffset;
    public uint rankSize;
    public uint numRanks;
    public uint rankOffset;
}