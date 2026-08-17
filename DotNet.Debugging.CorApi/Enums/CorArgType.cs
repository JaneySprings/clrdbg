namespace DotNet.Debugging.CorApi;

// Like: https://learn.microsoft.com/dotnet/framework/unmanaged-api/metadata/corargtype-enumeration
public enum CorArgType {
    END,
    VOID,
    I4,
    I8,
    R4,
    R8,
    PTR,
    OBJECT,
    STRUCT4,
    STRUCT32,
    BYVALUE
}