namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/corgcreferencetype-enumeration
public enum CorGCReferenceType {
    CorHandleStrong = 1 << 0,
    CorHandleStrongPinning = 1 << 1,
    CorHandleWeakShort = 1 << 2,
    CorHandleWeakLong = 1 << 3,
    CorHandleWeakRefCount = 1 << 4,
    CorHandleStrongRefCount = 1 << 5,
    CorHandleStrongDependent = 1 << 6,
    CorHandleStrongAsyncPinned = 1 << 7,
    CorHandleStrongSizedByref = 1 << 8,
    CorHandleWeakNativeCom = 1 << 9,
    CorHandleWeakWinRT = CorHandleWeakNativeCom,
    CorReferenceStack = unchecked((int)0x80000001),
    CorReferenceFinalizer = unchecked((int)0x80000002),
    CorHandleStrongOnly = 0x1E3,
    CorHandleWeakOnly = 0x21C,
    CorHandleAll = 0x7FFFFFFF
}