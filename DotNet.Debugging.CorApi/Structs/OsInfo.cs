namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/structures/osinfo-structure
public struct OsInfo {
    public uint dwOSPlatformId;
    public uint dwOSMajorVersion;
    public uint dwOSMinorVersion;
}