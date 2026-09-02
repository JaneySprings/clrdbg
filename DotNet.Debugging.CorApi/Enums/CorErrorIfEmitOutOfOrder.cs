namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corerrorifemitoutoforder-enumeration
public enum CorErrorIfEmitOutOfOrder {
    MDErrorOutOfOrderDefault = 0x00000000,
    MDErrorOutOfOrderNone = MDErrorOutOfOrderDefault,
    MDErrorOutOfOrderAll = unchecked((int)0xffffffff),
    MDMethodOutOfOrder = 0x00000001,
    MDFieldOutOfOrder = 0x00000002,
    MDParamOutOfOrder = 0x00000004,
    MDPropertyOutOfOrder = 0x00000008,
    MDEventOutOfOrder = 0x00000010
}