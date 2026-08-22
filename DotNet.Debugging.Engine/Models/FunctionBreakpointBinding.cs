using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine.Models;

internal class FunctionBreakpointBinding {
    public ICorDebugFunctionBreakpoint CorBreakpoint { get; }
    public CordbAddress ModuleBaseAddress { get; }
    public int MethodToken { get; }

    public FunctionBreakpointBinding(ICorDebugFunctionBreakpoint corBreakpoint, CordbAddress moduleBaseAddress, int methodToken) {
        CorBreakpoint = corBreakpoint;
        ModuleBaseAddress = moduleBaseAddress;
        MethodToken = methodToken;
    }
}
