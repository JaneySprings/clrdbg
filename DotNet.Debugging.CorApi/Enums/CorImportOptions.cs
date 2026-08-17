namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corimportoptions-enumeration
public enum CorImportOptions {
    MDImportOptionDefault = 0,
    MDImportOptionAll = -1,
    MDImportOptionAllTypeDefs = 1,
    MDImportOptionAllMethodDefs = 2,
    MDImportOptionAllFieldDefs = 4,
    MDImportOptionAllProperties = 8,
    MDImportOptionAllEvents = 16,
    MDImportOptionAllCustomAttributes = 32,
    MDImportOptionAllExportedTypes = 64
}