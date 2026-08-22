using System.Reflection.Metadata.Ecma335;
using DotNet.Debugging.Engine.Metadata;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Breakpoints;

internal static class FunctionBreakpointResolver {
    // Every method of the module matching the pattern, located at its first sequence point
    public static List<ResolvedBreakpoint> Resolve(ModuleMetadataReader metadataReader, FunctionBreakpointPattern pattern) {
        var result = new List<ResolvedBreakpoint>();
        var reader = metadataReader.PeMetadataReader;
        foreach (var typeHandle in reader.TypeDefinitions) {
            if (!pattern.MatchesType(TypeNameSignatureProvider.GetTypeName(reader, typeHandle)))
                continue;

            foreach (var methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods()) {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != pattern.MethodName)
                    continue;
                if (pattern.MethodArity != null && method.GetGenericParameters().Count != pattern.MethodArity)
                    continue;
                if (!pattern.MatchesParameters(method.DecodeSignature(TypeNameSignatureProvider.Instance, null).ParameterTypes))
                    continue;

                var resolved = metadataReader.ResolveMethodEntry(MetadataTokens.GetToken(methodHandle));
                if (resolved != null)
                    result.Add(resolved);
            }
        }
        return result;
    }
}
