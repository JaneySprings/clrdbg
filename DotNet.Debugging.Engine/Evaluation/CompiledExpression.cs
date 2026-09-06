using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotNet.Debugging.Engine.Extensions;

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
        if (!MetadataReader.TryFindMethodDefinition(typeName, methodName, out var entryMethod))
            throw new InvalidOperationException($"The generated evaluation method '{typeName}.{methodName}' was not found");
        EntryMethod = entryMethod;
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
}
