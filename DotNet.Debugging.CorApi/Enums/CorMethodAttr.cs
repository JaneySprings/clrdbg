namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cormethodattr-enumeration
public enum CorMethodAttr {
    mdMemberAccessMask = 7,
    mdPrivateScope = 0,
    mdPrivate = 1,
    mdFamANDAssem = 2,
    mdAssem = 3,
    mdFamily = 4,
    mdFamORAssem = 5,
    mdPublic = 6,
    mdStatic = 16,
    mdFinal = 32,
    mdVirtual = 64,
    mdHideBySig = 128,
    mdVtableLayoutMask = 256,
    mdReuseSlot = mdPrivateScope,
    mdNewSlot = mdVtableLayoutMask,
    mdCheckAccessOnOverride = 512,
    mdAbstract = 1024,
    mdSpecialName = 2048,
    mdPinvokeImpl = 8192,
    mdUnmanagedExport = 8,
    mdReservedMask = 53248,
    mdRTSpecialName = 4096,
    mdHasSecurity = 16384,
    mdRequireSecObject = 32768
}