using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DotNet.Debugging.CorApi.Extensions;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class EnumExtensions {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTdNested(this CorTypeAttr attr) {
        return (attr & CorTypeAttr.tdVisibilityMask) >= CorTypeAttr.tdNestedPublic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMdPublic(this CorMethodAttr attr) {
        return (attr & CorMethodAttr.mdMemberAccessMask) == CorMethodAttr.mdPublic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMdPrivate(this CorMethodAttr attr) {
        return (attr & CorMethodAttr.mdMemberAccessMask) == CorMethodAttr.mdPrivate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMdStatic(this CorMethodAttr attr) {
        return (attr & CorMethodAttr.mdStatic) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMdVirtual(this CorMethodAttr attr) {
        return (attr & CorMethodAttr.mdVirtual) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMdSpecialName(this CorMethodAttr attr) {
        return (attr & CorMethodAttr.mdSpecialName) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMdNewSlot(this CorMethodAttr attr) {
        return (attr & CorMethodAttr.mdVtableLayoutMask) == CorMethodAttr.mdVtableLayoutMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFdPublic(this CorFieldAttr attr) {
        return (attr & CorFieldAttr.fdFieldAccessMask) == CorFieldAttr.fdPublic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFdStatic(this CorFieldAttr attr) {
        return (attr & CorFieldAttr.fdStatic) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFdLiteral(this CorFieldAttr attr) {
        return (attr & CorFieldAttr.fdLiteral) != 0;
    }
}