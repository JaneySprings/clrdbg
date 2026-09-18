using System.Collections.Generic;

namespace DotNet.Debugging.Remote.Generator;

// The getters whose answer cannot change for the life of the object they are asked of: a type's class and base, a
// function's token, a module's base address, a frame's function. A proxy asks the agent once and remembers the
// answer, which is most of what a debugger asks again and again while it lists variables
internal static class ImmutableResults {
    private static readonly HashSet<string> getters = new HashSet<string> {
        "ICorDebugProcess.TryGetID",
        "ICorDebugAppDomain.TryGetProcess",
        "ICorDebugAppDomain.TryGetID",
        "ICorDebugAssembly.TryGetProcess",
        "ICorDebugAssembly.TryGetAppDomain",
        "ICorDebugModule.TryGetProcess",
        "ICorDebugModule.TryGetBaseAddress",
        "ICorDebugModule.TryGetAssembly",
        "ICorDebugModule.TryGetToken",
        "ICorDebugModule.TryIsDynamic",
        "ICorDebugModule.TryGetSize",
        "ICorDebugThread.TryGetProcess",
        "ICorDebugThread.TryGetID",
        "ICorDebugFunction.TryGetModule",
        "ICorDebugFunction.TryGetClass",
        "ICorDebugFunction.TryGetToken",
        "ICorDebugClass.TryGetModule",
        "ICorDebugClass.TryGetToken",
        "ICorDebugCode.TryIsIL",
        "ICorDebugCode.TryGetFunction",
        "ICorDebugCode.TryGetAddress",
        "ICorDebugCode.TryGetSize",
        "ICorDebugType.TryGetType",
        "ICorDebugType.TryGetClass",
        "ICorDebugType.TryGetFirstTypeParameter",
        "ICorDebugType.TryGetBase",
        "ICorDebugType.TryGetRank",
        "ICorDebugFrame.TryGetCode",
        "ICorDebugFrame.TryGetFunction",
        "ICorDebugFrame.TryGetFunctionToken",
    };

    public static bool Contains(CorApiInterface iface, CorApiMethod method) {
        return getters.Contains(iface.Name + "." + method.Name);
    }
}
