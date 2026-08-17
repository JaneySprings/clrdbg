namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-type-layout-structure
public struct CorTypeLayout {
    public CorTypeId parentID;
    public uint objectSize;
    public uint numFields;
    public uint boxOffset;
    public CorElementType type;
}