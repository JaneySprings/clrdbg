using System.Runtime.CompilerServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugModuleManualExtensions {
    // Asking a module for an importer is a QueryInterface that hands out a new wrapper (with a native reference
    // of its own to release) every time, while the importer stays the same for as long as the module's metadata stands
    private static readonly ConditionalWeakTable<ICorDebugModule, Dictionary<Type, object>> metaDataInterfaces = new ConditionalWeakTable<ICorDebugModule, Dictionary<Type, object>>();

    public static T GetMetaDataInterface<T>(this ICorDebugModule module) where T : class {
        var cache = metaDataInterfaces.GetOrCreateValue(module);
        lock (cache) {
            if (cache.TryGetValue(typeof(T), out var cached))
                return (T)cached;

            var hr = module.TryGetMetaDataInterface<T>(out var metadata);
            if (hr < 0 || metadata == null)
                throw new InvalidOperationException($"Failed to get metadata interface of type {typeof(T).FullName} from module {module.GetName()}. HRESULT: 0x{hr:X8}");
            cache[typeof(T)] = metadata;
            return metadata;
        }
    }

    public static int TryGetMetaDataInterface<T>(this ICorDebugModule module, out T? metadata) where T : class {
        var riid = typeof(T).GUID;
        var hr = module.TryGetMetaDataInterface(ref riid, out var ppObj);
        metadata = hr >= 0 ? (T?)ppObj : null;
        return hr;
    }

    // The runtime rebuilds the metadata of a dynamic module whenever a type gets defined in it: the importers
    // handed out before that see the old metadata and are forgotten here
    public static void ResetMetaDataInterfaces(this ICorDebugModule module) {
        metaDataInterfaces.Remove(module);
    }
}
