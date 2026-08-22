using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotNet.Debugging.Engine.Evaluation;

// The assembly the Roslyn expression compiler emitted for an expression, whose entry method is run by the CIL interpreter
internal class CompiledExpression : IDisposable {
    private readonly MemoryStream peStream;
    private readonly Dictionary<MethodDefinitionHandle, DecodedMethod> decodedMethods = new Dictionary<MethodDefinitionHandle, DecodedMethod>();

    public PEReader PeReader { get; }
    public MetadataReader MetadataReader { get; }
    public MethodDefinitionHandle EntryMethod { get; }

    public CompiledExpression(byte[] assembly, string typeName, string methodName) {
        peStream = new MemoryStream(assembly, writable: false);
        PeReader = new PEReader(peStream);
        MetadataReader = PeReader.GetMetadataReader();
        EntryMethod = FindMethod(MetadataReader, typeName, methodName);
    }

    public MethodBodyBlock GetMethodBody(MethodDefinitionHandle handle) {
        return PeReader.GetMethodBody(MetadataReader.GetMethodDefinition(handle).RelativeVirtualAddress);
    }
    public DecodedMethod GetDecodedMethod(MethodDefinitionHandle handle) {
        if (decodedMethods.TryGetValue(handle, out var decoded))
            return decoded;

        var il = GetMethodBody(handle).GetILBytes();
        ArgumentNullException.ThrowIfNull(il);
        decoded = CilInstructionDecoder.Decode(il);
        decodedMethods[handle] = decoded;
        return decoded;
    }

    public void Dispose() {
        PeReader.Dispose();
        peStream.Dispose();
    }

    private static MethodDefinitionHandle FindMethod(MetadataReader reader, string typeName, string methodName) {
        foreach (var typeHandle in reader.TypeDefinitions) {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (var methodHandle in type.GetMethods()) {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                    return methodHandle;
            }
        }
        throw new InvalidOperationException($"The generated evaluation method '{typeName}.{methodName}' was not found");
    }
}
