using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote.Proxy;

// A native frame without IL, such as a P/Invoke stub
[GeneratedComClass]
internal partial class RemoteCorDebugNativeFrame : RemoteObject, ICorDebugFrame, ICorDebugNativeFrame, ICorDebugNativeFrame2 { }
