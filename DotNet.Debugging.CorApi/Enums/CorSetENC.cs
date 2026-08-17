namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corsetenc-enumeration
public enum CorSetENC {
    MDSetENCOn = 1,
    MDSetENCOff = 2,
    MDUpdateENC = MDSetENCOn,
    MDUpdateFull = MDSetENCOff,
    MDUpdateExtension = 3,
    MDUpdateIncremental = 4,
    MDUpdateDelta = 5,
    MDUpdateMask = 7
}