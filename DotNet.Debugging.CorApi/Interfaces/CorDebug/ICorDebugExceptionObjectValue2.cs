using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("E3B2F332-CC46-4F1E-AB4E-5400E332195E")]
public partial interface ICorDebugExceptionObjectValue2 {
    [PreserveSig]
    int TryForceCatchHandlerFoundEvents([MarshalAs(UnmanagedType.Bool)] bool enableEvents);
}