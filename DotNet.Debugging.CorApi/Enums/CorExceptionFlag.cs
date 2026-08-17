namespace DotNet.Debugging.CorApi;

public enum CorExceptionFlag {
    NONE = 0,
    FILTER = 1,
    FINALLY = 2,
    FAULT = 4,
    DUPLICATED = 8,
    SAMETRY = 0x10,
    R2R_SYSTEM_EXCEPTION = 0x20
}