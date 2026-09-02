namespace DotNet.Debugging.CorApi;

public enum CorExceptionFlag {
    NONE = 0,
    FILTER = 0x0001,
    FINALLY = 0x0002,
    FAULT = 0x0004,
    DUPLICATED = 0x0008,
    SAMETRY = 0x10,
    R2R_SYSTEM_EXCEPTION = 0x20
}