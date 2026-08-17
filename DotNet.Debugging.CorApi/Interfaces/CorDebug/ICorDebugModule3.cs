using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmodule3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("86F012BF-FF15-4372-BD30-B6F11CAAE1DD")]
public partial interface ICorDebugModule3 {
    [PreserveSig]
    int TryCreateReaderForInMemorySymbols(ref Guid riid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<object>))] out object? ppObj);
}