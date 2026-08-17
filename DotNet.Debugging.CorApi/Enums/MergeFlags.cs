namespace DotNet.Debugging.CorApi;

public enum MergeFlags {
    None = 0,
    MergeManifest = 1,
    DropMemberRefCAs = 2,
    NoDupCheck = 4,
    MergeExportedTypes = 8
}