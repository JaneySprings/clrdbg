using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote.Proxy;

// A frame of jitted code: a native frame with an IL frame behind it, reached through the same handle
[GeneratedComClass]
internal partial class RemoteCorDebugManagedFrame : RemoteObject, ICorDebugFrame, ICorDebugILFrame, ICorDebugILFrame2, ICorDebugILFrame3, ICorDebugILFrame4, ICorDebugNativeFrame, ICorDebugNativeFrame2 { }
