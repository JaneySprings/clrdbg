using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("E799DC06-E099-4713-BDD9-906D3CC02CF2")]
public partial interface ICorDebugDataTarget4 {
    [PreserveSig]
    int TryVirtualUnwind(uint threadId, uint contextSize, [In][Out][MarshalUsing(CountElementName = "contextSize")] byte[] context);
}