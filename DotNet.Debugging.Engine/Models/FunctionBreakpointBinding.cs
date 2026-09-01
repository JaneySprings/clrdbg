using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine.Models;

internal class FunctionBreakpointBinding {
    public ICorDebugFunctionBreakpoint CorBreakpoint { get; }
    public ICorDebugModule Module { get; }
    public int MethodToken { get; }

    public FunctionBreakpointBinding(ICorDebugFunctionBreakpoint corBreakpoint, ICorDebugModule module, int methodToken) {
        CorBreakpoint = corBreakpoint;
        Module = module;
        MethodToken = methodToken;
    }
}
