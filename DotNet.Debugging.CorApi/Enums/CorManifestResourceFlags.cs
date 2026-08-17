namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cormanifestresourceflags-enumeration
public enum CorManifestResourceFlags {
    mrVisibilityMask = 7,
    mrPublic = 1,
    mrPrivate = 2
}