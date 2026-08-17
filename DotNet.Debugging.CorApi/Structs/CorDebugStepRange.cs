namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-debug-step-range-structure
public struct CorDebugStepRange {
    public uint startOffset;
    public uint endOffset;
}