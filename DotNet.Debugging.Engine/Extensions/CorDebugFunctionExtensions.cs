using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Metadata;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugFunctionExtensions {
    // 'Namespace.Type.Method(string[] args)' from the PE metadata; a nested type carries its enclosing types, which keeps
    // the state machines of two methods of one name apart ('Worker.<RunAsync>d__0.MoveNext()')
    public static string GetDisplayName(this ICorDebugFunction function, MetadataReader peReader) {
        try {
            var token = function.GetToken();
            var method = peReader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(token));
            var typeName = peReader.GetTypeName(method.GetDeclaringType());
            return $"{typeName}.{peReader.GetString(method.Name)}({peReader.GetParameterList(token, DisplayNameSignatureProvider.Instance)})";
        }
        catch {
            return "Unknown";
        }
    }
}
