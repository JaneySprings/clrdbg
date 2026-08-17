namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-debug-il-to-native-map-structure
public struct CorDebugIlToNativeMap {
    public uint ilOffset;
    public uint nativeStartOffset;
    public uint nativeEndOffset;
}