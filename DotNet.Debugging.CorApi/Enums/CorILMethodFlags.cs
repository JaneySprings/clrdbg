namespace DotNet.Debugging.CorApi;

public enum CorILMethodFlags {
    InitLocals = 16,
    MoreSects = 8,
    CompressedIL = 64,
    FormatShift = 3,
    FormatMask = 7,
    TinyFormat = 2,
    SmallFormat = 0,
    FatFormat = FormatShift,
    TinyFormat1 = 6
}