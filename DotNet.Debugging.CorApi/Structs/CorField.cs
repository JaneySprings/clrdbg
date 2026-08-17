namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-field-structure
public struct CorField {
    public FieldDefToken token;
    public uint offset;
    public CorTypeId id;
    public CorElementType fieldType;
}