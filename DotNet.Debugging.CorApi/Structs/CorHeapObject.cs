namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-heapobject-structure
public struct CorHeapObject {
    public CordbAddress address;
    public ulong size;
    public CorTypeId type;
}