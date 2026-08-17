namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/corgcreferencetype-enumeration
public enum CorGCReferenceType {
    CorHandleStrong = 1,
    CorHandleStrongPinning = 2,
    CorHandleWeakShort = 4,
    CorHandleWeakLong = 8,
    CorHandleWeakRefCount = 16,
    CorHandleStrongRefCount = 32,
    CorHandleStrongDependent = 64,
    CorHandleStrongAsyncPinned = 128,
    CorHandleStrongSizedByref = 256,
    CorHandleWeakNativeCom = 512,
    CorHandleWeakWinRT = CorHandleWeakNativeCom,
    CorReferenceStack = -2147483647,
    CorReferenceFinalizer = 80000002,
    CorHandleStrongOnly = 483,
    CorHandleWeakOnly = 540,
    CorHandleAll = int.MaxValue
}