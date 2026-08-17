namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corsavesize-enumeration
public enum CorSaveSize {
    cssAccurate,
    cssQuick,
    cssDiscardTransientCAs
}