namespace DotNet.Debugging.CorApi;

public enum CorILMethodSect {
    Reserved = 0,
    EHTable = 1,
    OptILTable = 2,
    KindMask = 63,
    FatFormat = 64,
    MoreSects = 128
}