namespace DotNet.Debugging.CorApi;

public enum CorILMethodSect {
    Reserved = 0,
    EHTable = 1,
    OptILTable = 2,
    KindMask = 0x3F,
    FatFormat = 0x40,
    MoreSects = 0x80
}