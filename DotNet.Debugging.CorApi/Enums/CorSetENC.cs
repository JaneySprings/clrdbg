namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corsetenc-enumeration
public enum CorSetENC {
    MDSetENCOn = 0x00000001,
    MDSetENCOff = 0x00000002,
    MDUpdateENC = MDSetENCOn,
    MDUpdateFull = MDSetENCOff,
    MDUpdateExtension = 0x00000003,
    MDUpdateIncremental = 0x00000004,
    MDUpdateDelta = 0x00000005,
    MDUpdateMask = 0x00000007
}