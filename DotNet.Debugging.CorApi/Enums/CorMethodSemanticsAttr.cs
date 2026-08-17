namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cormethodsemanticsattr-enumeration
public enum CorMethodSemanticsAttr {
    msSetter = 1,
    msGetter = 2,
    msOther = 4,
    msAddOn = 8,
    msRemoveOn = 0x10,
    msFire = 0x20
}