namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugModuleManualExtensions {
    public static T GetMetaDataInterface<T>(this ICorDebugModule module) where T : class {
        var hr = module.TryGetMetaDataInterface<T>(out var metadata);
        if (hr < 0 || metadata == null) {
            throw new InvalidOperationException($"Failed to get metadata interface of type {typeof(T).FullName} from module {module.GetName()}. HRESULT: 0x{hr:X8}");
        }
        return metadata;
    }

    public static int TryGetMetaDataInterface<T>(this ICorDebugModule module, out T? metadata) where T : class {
        var riid = typeof(T).GUID;
        var hr = module.TryGetMetaDataInterface(ref riid, out var ppObj);
        metadata = hr >= 0 ? (T?)ppObj : null;
        return hr;
    }
}