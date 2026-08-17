namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cormethodimpl-enumeration
public enum CorMethodImpl {
    miCodeTypeMask = 3,
    miIL = 0,
    miNative = 1,
    miOPTIL = 2,
    miRuntime = miCodeTypeMask,
    miManagedMask = 4,
    miUnmanaged = miManagedMask,
    miManaged = miIL,
    miForwardRef = 16,
    miPreserveSig = 128,
    miInternalCall = 4096,
    miSynchronized = 32,
    miNoInlining = 8,
    miAggressiveInlining = 256,
    miNoOptimization = 64,
    miAggressiveOptimization = 512,
    miAsync = 8192,
    miUserMask = 13308,
    miMaxMethodImplVal = 65535
}