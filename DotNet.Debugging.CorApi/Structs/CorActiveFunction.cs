using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-active-function-structure
[NativeMarshalling(typeof(CorActiveFunctionMarshaller))]
public struct CorActiveFunction {
    public ICorDebugAppDomain pAppDomain;
    public ICorDebugModule pModule;
    public ICorDebugFunction2 pFunction;
    public uint ilOffset;
    public uint flags;
}