namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cornotificationfortokenmovement-enumeration
public enum CorNotificationForTokenMovement {
    MDNotifyDefault = 15,
    MDNotifyAll = -1,
    MDNotifyNone = 0,
    MDNotifyMethodDef = 1,
    MDNotifyMemberRef = 2,
    MDNotifyFieldDef = 4,
    MDNotifyTypeRef = 8,
    MDNotifyTypeDef = 16,
    MDNotifyParamDef = 32,
    MDNotifyInterfaceImpl = 64,
    MDNotifyProperty = 128,
    MDNotifyEvent = 256,
    MDNotifySignature = 512,
    MDNotifyTypeSpec = 1024,
    MDNotifyCustomAttribute = 2048,
    MDNotifySecurityValue = 4096,
    MDNotifyPermission = 8192,
    MDNotifyModuleRef = 16384,
    MDNotifyNameSpace = 32768,
    MDNotifyAssemblyRef = 16777216,
    MDNotifyFile = 33554432,
    MDNotifyExportedType = 67108864,
    MDNotifyResource = 134217728
}