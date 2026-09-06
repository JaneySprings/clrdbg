using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Metadata;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugModuleExtensions {
    public static void TrySetMethodNotUserCode(this ICorDebugModule module, int methodToken) {
        try {
            if (module.GetFunctionFromToken(new MethodDefToken((uint)methodToken)) is ICorDebugFunction2 function)
                function.TrySetJMCStatus(false);
        }
        catch {
            // A method without a body cannot be resolved - nothing to mark
        }
    }
    // A dynamic module has no image to read, its metadata is taken from the runtime's own importer. The importer
    // is rebuilt whenever a type gets defined in the module, so the metadata is copied rather than referenced
    public static ModuleMetadataReader? TryLoadDynamicMetadata(this ICorDebugModule module) {
        // A module the debuggee has only just created has nothing for the runtime to hand out yet
        if (module.TryGetMetaDataInterface<IMetaDataTables2>(out var tables) < 0 || tables == null)
            return null;
        var (pointer, size) = tables.GetMetaDataStorage();
        var metadataReader = ModuleMetadataReader.TryLoad(pointer, size);
        // The storage belongs to the importer, which must not be released before the copy is taken
        GC.KeepAlive(tables);
        return metadataReader;
    }
}
