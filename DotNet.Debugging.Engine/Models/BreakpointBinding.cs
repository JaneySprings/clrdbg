using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine.Models;

// A breakpoint's runtime breakpoint at one IL location, shared with every other breakpoint bound there
internal class BreakpointBinding {
    public ICorDebugFunctionBreakpoint CorBreakpoint { get; }
    public ICorDebugModule Module { get; }
    public int MethodToken { get; }
    public int ILOffset { get; }

    public BreakpointBinding(ICorDebugFunctionBreakpoint corBreakpoint, ICorDebugModule module, int methodToken, int ilOffset) {
        CorBreakpoint = corBreakpoint;
        Module = module;
        MethodToken = methodToken;
        ILOffset = ilOffset;
    }

    public bool IsAt(ICorDebugModule module, int methodToken, int ilOffset) {
        return Module == module && MethodToken == methodToken && ILOffset == ilOffset;
    }
}
