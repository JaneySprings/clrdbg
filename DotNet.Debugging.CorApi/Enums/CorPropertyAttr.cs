namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corpropertyattr-enumeration
public enum CorPropertyAttr {
    prSpecialName = 512,
    prReservedMask = 62464,
    prRTSpecialName = 1024,
    prHasDefault = 4096,
    prUnused = 59903
}