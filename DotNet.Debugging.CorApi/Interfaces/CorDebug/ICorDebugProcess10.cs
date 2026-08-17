using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("8F378F6F-1017-4461-9890-ECF64C54079F")]
public partial interface ICorDebugProcess10 {
    [PreserveSig]
    int TryEnableGCNotificationEvents([MarshalAs(UnmanagedType.Bool)] bool fEnable);
}