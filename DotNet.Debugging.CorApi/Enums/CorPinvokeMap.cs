namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corpinvokemap-enumeration
public enum CorPinvokeMap {
    pmNoMangle = 1,
    pmCharSetMask = 6,
    pmCharSetNotSpec = 0,
    pmCharSetAnsi = 2,
    pmCharSetUnicode = 4,
    pmCharSetAuto = pmCharSetMask,
    pmBestFitUseAssem = pmCharSetNotSpec,
    pmBestFitEnabled = 16,
    pmBestFitDisabled = 32,
    pmBestFitMask = 48,
    pmThrowOnUnmappableCharUseAssem = pmCharSetNotSpec,
    pmThrowOnUnmappableCharEnabled = 4096,
    pmThrowOnUnmappableCharDisabled = 8192,
    pmThrowOnUnmappableCharMask = 12288,
    pmSupportsLastError = 64,
    pmCallConvMask = 1792,
    pmCallConvWinapi = 256,
    pmCallConvCdecl = 512,
    pmCallConvStdcall = 768,
    pmCallConvThiscall = 1024,
    pmCallConvFastcall = 1280,
    pmMaxValue = 65535
}