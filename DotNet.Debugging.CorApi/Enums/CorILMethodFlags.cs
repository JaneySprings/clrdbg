namespace DotNet.Debugging.CorApi;

public enum CorILMethodFlags {
    InitLocals = 0x0010,
    MoreSects = 0x0008,
    CompressedIL = 0x0040,
    FormatShift = 3,
    FormatMask = (1 << FormatShift) - 1,
    TinyFormat = 0x0002,
    SmallFormat = 0x0000,
    FatFormat = FormatShift,
    TinyFormat1 = 0x0006
}