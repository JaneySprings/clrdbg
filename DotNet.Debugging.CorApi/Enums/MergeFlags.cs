namespace DotNet.Debugging.CorApi;

public enum MergeFlags {
    None = 0,
    MergeManifest = 0x00000001,
    DropMemberRefCAs = 0x00000002,
    NoDupCheck = 0x00000004,
    MergeExportedTypes = 0x00000008
}