using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("9D4DAB7B-3401-4F37-BD08-CA09F3FDF10F")]
public partial interface ICorDebugFunction5 {
    [PreserveSig]
    int TryDisableOptimizations();

    [PreserveSig]
    int TryAreOptimizationsDisabled([MarshalAs(UnmanagedType.Bool)] out bool pOptimizationsDisabled);
}