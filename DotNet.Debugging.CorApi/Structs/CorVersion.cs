namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-version-structure
public struct CorVersion {
    public uint dwMajor;
    public uint dwMinor;
    public uint dwBuild;
    public uint dwSubBuild;
}