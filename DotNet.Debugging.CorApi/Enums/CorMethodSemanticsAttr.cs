namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cormethodsemanticsattr-enumeration
public enum CorMethodSemanticsAttr {
    msSetter = 0x0001,
    msGetter = 0x0002,
    msOther = 0x0004,
    msAddOn = 0x0008,
    msRemoveOn = 0x10,
    msFire = 0x20
}