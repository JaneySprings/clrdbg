namespace DotNet.Debugging.CorApi;

public enum CorFieldAttr {
    fdFieldAccessMask = 7,
    fdPrivateScope = 0,
    fdPrivate = 1,
    fdFamANDAssem = 2,
    fdAssembly = 3,
    fdFamily = 4,
    fdFamORAssem = 5,
    fdPublic = 6,
    fdStatic = 16,
    fdInitOnly = 32,
    fdLiteral = 64,
    fdNotSerialized = 128,
    fdSpecialName = 512,
    fdPinvokeImpl = 8192,
    fdReservedMask = 38144,
    fdRTSpecialName = 1024,
    fdHasFieldMarshal = 4096,
    fdHasDefault = 32768,
    fdHasFieldRVA = 256
}