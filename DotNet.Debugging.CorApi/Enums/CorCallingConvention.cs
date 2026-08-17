namespace DotNet.Debugging.CorApi;

public enum CorCallingConvention {
    DEFAULT = 0,
    C = 1,
    STDCALL = 2,
    THISCALL = 3,
    FASTCALL = 4,
    VARARG = 5,
    FIELD = 6,
    LOCAL_SIG = 7,
    PROPERTY = 8,
    UNMANAGED = 9,
    GENERICINST = 10,
    NATIVEVARARG = 11,
    ASYNC = 12,
    MAX = 13,
    MASK = 15,
    HASTHIS = 32,
    EXPLICITTHIS = 64,
    GENERIC = 16
}