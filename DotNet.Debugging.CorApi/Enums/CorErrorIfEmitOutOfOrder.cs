namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corerrorifemitoutoforder-enumeration
public enum CorErrorIfEmitOutOfOrder {
    MDErrorOutOfOrderDefault = 0,
    MDErrorOutOfOrderNone = MDErrorOutOfOrderDefault,
    MDErrorOutOfOrderAll = -1,
    MDMethodOutOfOrder = 1,
    MDFieldOutOfOrder = 2,
    MDParamOutOfOrder = 4,
    MDPropertyOutOfOrder = 8,
    MDEventOutOfOrder = 16
}