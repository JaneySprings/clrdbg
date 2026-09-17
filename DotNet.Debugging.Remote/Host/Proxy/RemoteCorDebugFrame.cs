using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote.Proxy;

// A frame of no known kind: neither IL, native, internal nor runtime-unwindable
[GeneratedComClass]
internal partial class RemoteCorDebugFrame : RemoteObject, ICorDebugFrame { }
