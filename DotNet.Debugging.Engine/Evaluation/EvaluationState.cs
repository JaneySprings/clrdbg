using System.Reflection.Metadata;

namespace DotNet.Debugging.Engine.Evaluation;

// What one evaluation accumulates across the methods it interprets: the variables the expression declares and the
// static state of the expression assembly's own types (a closure class's cached instance and delegates)
internal class EvaluationState {
    private readonly Dictionary<FieldDefinitionHandle, ICilLocation> staticFields = new Dictionary<FieldDefinitionHandle, ICilLocation>();

    public Dictionary<string, ICilLocation> SyntheticVariables { get; } = new Dictionary<string, ICilLocation>(StringComparer.Ordinal);
    // The expression assembly's types whose static constructor ran (or was found absent)
    public HashSet<TypeDefinitionHandle> InitializedTypes { get; } = new HashSet<TypeDefinitionHandle>();

    public ICilLocation GetStaticField(FieldDefinitionHandle field) {
        if (!staticFields.TryGetValue(field, out var location)) {
            location = new TemporaryLocation(CilValue.Null());
            staticFields[field] = location;
        }
        return location;
    }
}
