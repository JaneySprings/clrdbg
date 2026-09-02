namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corpekind-enumeration
public enum CorPEKind {
    peNot = 0x00000000,
    peILonly = 0x00000001,
    pe32BitRequired = 0x00000002,
    pe32Plus = 0x00000004,
    pe32Unmanaged = 0x00000008,
    pe32BitPreferred = 0x10
}