using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote.Proxy;

// A primitive value: its bytes are read and written through the debugger's own buffers
[GeneratedComClass]
internal partial class RemoteCorDebugGenericValue : RemoteValueBytes, ICorDebugValue, ICorDebugValue2, ICorDebugValue3, ICorDebugGenericValue { }
