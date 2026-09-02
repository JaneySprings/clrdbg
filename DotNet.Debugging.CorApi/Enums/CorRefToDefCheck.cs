namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/correftodefcheck-enumeration
public enum CorRefToDefCheck {
    MDRefToDefDefault = 0x00000003,
    MDRefToDefAll = unchecked((int)0xffffffff),
    MDRefToDefNone = 0x00000000,
    MDTypeRefToDef = 0x00000001,
    MDMemberRefToDef = 0x00000002
}