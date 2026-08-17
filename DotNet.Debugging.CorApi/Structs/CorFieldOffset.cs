namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/structures/cor-field-offset-structure
public struct CorFieldOffset {
    public uint ridOfField;
    public uint ulOffset;
}