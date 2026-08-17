namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-typeid-structure
public struct CorTypeId {
    public ulong token1;
    public ulong token2;
}