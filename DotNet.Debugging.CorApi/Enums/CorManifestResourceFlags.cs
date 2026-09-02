namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cormanifestresourceflags-enumeration
public enum CorManifestResourceFlags {
    mrVisibilityMask = 0x0007,
    mrPublic = 0x0001,
    mrPrivate = 0x0002
}