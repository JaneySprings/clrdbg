namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/coropenflags-enumeration
public enum CorOpenFlags {
    ofRead = 0,
    ofWrite = 1,
    ofReadWriteMask = ofWrite,
    ofCopyMemory = 2,
    ofReadOnly = 16,
    ofTakeOwnership = 32,
    ofNoTypeLib = 128,
    ofNoTransform = 4096,
    ofReserved1 = 256,
    ofReserved2 = 512,
    ofReserved3 = 1024,
    ofReserved = -4288
}