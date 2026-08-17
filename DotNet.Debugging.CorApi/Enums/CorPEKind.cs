namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corpekind-enumeration
public enum CorPEKind {
    peNot = 0,
    peILonly = 1,
    pe32BitRequired = 2,
    pe32Plus = 4,
    pe32Unmanaged = 8,
    pe32BitPreferred = 0x10
}