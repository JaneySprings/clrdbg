using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugILFrameExtensions {
    // The '<>t__builder' field of the state machine 'this', null when the frame is not an async method's MoveNext
    public static ICorDebugValue? GetAsyncMethodBuilder(this ICorDebugILFrame frame) {
        try {
            var function = frame.GetFunction();
            var metadataImport = function.GetModule().GetMetaDataInterface<IMetaDataImport>();
            if (metadataImport.GetMethodProps(function.GetToken()).pdwAttr.IsMdStatic())
                return null;

            var arguments = frame.GetArguments();
            if (arguments.Length == 0 || arguments[0] is not ICorDebugReferenceValue thisReference || thisReference.IsNull())
                return null;
            if (thisReference.Dereference() is not ICorDebugObjectValue thisObject)
                return null;
            return thisObject.FindFieldValue("<>t__builder")?.UnwrapDebugValue();
        }
        catch {
            return null;
        }
    }
}
