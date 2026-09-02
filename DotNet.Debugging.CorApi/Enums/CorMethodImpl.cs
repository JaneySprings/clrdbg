namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cormethodimpl-enumeration
public enum CorMethodImpl {
    miCodeTypeMask = 0x0003,
    miIL = 0x0000,
    miNative = 0x0001,
    miOPTIL = 0x0002,
    miRuntime = miCodeTypeMask,
    miManagedMask = 0x0004,
    miUnmanaged = miManagedMask,
    miManaged = miIL,
    miForwardRef = 0x0010,
    miPreserveSig = 0x0080,
    miInternalCall = 0x1000,
    miSynchronized = 0x0020,
    miNoInlining = 0x0008,
    miAggressiveInlining = 0x0100,
    miNoOptimization = 0x0040,
    miAggressiveOptimization = 0x0200,
    miAsync = 0x2000,
    miUserMask = miManagedMask | miForwardRef | miPreserveSig | miInternalCall | miSynchronized | miNoInlining | miAggressiveInlining | miNoOptimization | miAggressiveOptimization | miAsync,
    miMaxMethodImplVal = 0xffff
}