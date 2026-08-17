namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corcheckduplicatesfor-enumeration
public enum CorCheckDuplicatesFor {
    MDDupAll = -1,
    MDDupENC = MDDupAll,
    MDNoDupChecks = 0,
    MDDupTypeDef = 1,
    MDDupInterfaceImpl = 2,
    MDDupMethodDef = 4,
    MDDupTypeRef = 8,
    MDDupMemberRef = 16,
    MDDupCustomAttribute = 32,
    MDDupParamDef = 64,
    MDDupPermission = 128,
    MDDupProperty = 256,
    MDDupEvent = 512,
    MDDupFieldDef = 1024,
    MDDupSignature = 2048,
    MDDupModuleRef = 4096,
    MDDupTypeSpec = 8192,
    MDDupImplMap = 16384,
    MDDupAssemblyRef = 32768,
    MDDupFile = 65536,
    MDDupExportedType = 131072,
    MDDupManifestResource = 262144,
    MDDupGenericParam = 524288,
    MDDupMethodSpec = 1048576,
    MDDupGenericParamConstraint = 2097152,
    MDDupAssembly = 268435456,
    MDDupDefault = 1058840
}