using System.Reflection.Metadata;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Metadata;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugFunctionExtensions {
    // 'Namespace.Type.Method(string[] args)', the parameter list comes from the PE metadata
    public static string GetDisplayName(this ICorDebugFunction function, MetadataReader peReader) {
        try {
            var token = function.GetToken();
            var metadataImport = function.GetModule().GetMetaDataInterface<IMetaDataImport>();
            var methodName = metadataImport.GetMethodProps(token).szMethod;
            var typeName = metadataImport.GetTypeDefProps(function.GetClass().GetToken()).szTypeDef;
            return $"{typeName}.{methodName}({peReader.GetParameterList(token, DisplayNameSignatureProvider.Instance)})";
        }
        catch {
            return "Unknown";
        }
    }
}
