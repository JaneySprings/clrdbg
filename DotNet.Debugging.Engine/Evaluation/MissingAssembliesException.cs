namespace DotNet.Debugging.Engine.Evaluation;

// The expression names a type or an extension method of an assembly the debuggee has not loaded (System.Linq, for a
// program that never used it); the message is the compiler's, for when loading the assembly does not help either
public class MissingAssembliesException : EvaluationException {
    public IReadOnlyList<string> AssemblyNames { get; }

    public MissingAssembliesException(string message, IReadOnlyList<string> assemblyNames) : base(message) {
        AssemblyNames = assemblyNames;
    }
}
