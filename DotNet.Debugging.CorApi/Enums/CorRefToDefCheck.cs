namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/correftodefcheck-enumeration
public enum CorRefToDefCheck {
    MDRefToDefDefault = 3,
    MDRefToDefAll = -1,
    MDRefToDefNone = 0,
    MDTypeRefToDef = 1,
    MDMemberRefToDef = 2
}