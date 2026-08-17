namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-heapinfo-structure
public struct CorHeapInfo {
    public int areGCStructuresValid;
    public uint pointerSize;
    public uint numHeaps;
    public int concurrent;
    public CorDebugGCType gcType;
}