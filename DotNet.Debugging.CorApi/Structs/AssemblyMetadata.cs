namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/structures/assemblymetadata-structure
public struct AssemblyMetadata {
    public ushort usMajorVersion;
    public ushort usMinorVersion;
    public ushort usBuildNumber;
    public ushort usRevisionNumber;
    public unsafe char* szLocale;
    public uint cbLocale;
    public unsafe uint* rProcessor;
    public uint ulProcessor;
    public unsafe OsInfo* rOS;
    public uint ulOS;
}