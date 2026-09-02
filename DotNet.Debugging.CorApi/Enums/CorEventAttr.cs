namespace DotNet.Debugging.CorApi;

public enum CorEventAttr {
    evSpecialName = 0x0200,
    evReservedMask = 0x0400,
    evRTSpecialName = evReservedMask
}