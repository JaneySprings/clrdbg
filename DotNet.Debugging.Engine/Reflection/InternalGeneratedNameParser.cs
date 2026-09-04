using System.Reflection;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameParser: reads the kind out of a compiler generated name ('<>4__this', '<count>5__1', '<>c__DisplayClass0_0')
internal static class InternalGeneratedNameParser {
    public static Type Type { get; }
    private static readonly Type kindType;
    private static readonly MethodInfo getKindMethod;
    private static readonly MethodInfo tryParseGeneratedNameMethod;

    static InternalGeneratedNameParser() {
        Type = RoslynReflection.GetType(RoslynReflection.CSharpAssembly, "Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameParser");
        kindType = RoslynReflection.GetType(RoslynReflection.CSharpAssembly, "Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameKind");
        getKindMethod = RoslynReflection.GetMethod(Type, "GetKind", typeof(string));
        tryParseGeneratedNameMethod = RoslynReflection.GetMethod(Type, "TryParseGeneratedName", 4);
    }

    public static GeneratedNameKind GetKind(string name) {
        return ToKind(getKindMethod.InvokeUnwrapped(null, [name])!);
    }
    // The offsets delimit the original name inside the generated one ('<count>5__1' -> 'count')
    public static bool TryParseGeneratedName(string name, out GeneratedNameKind kind, out int openBracketOffset, out int closeBracketOffset) {
        var arguments = new object?[] { name, null, null, null };
        var parsed = (bool)tryParseGeneratedNameMethod.InvokeUnwrapped(null, arguments)!;
        kind = ToKind(arguments[1]!);
        openBracketOffset = (int)arguments[2]!;
        closeBracketOffset = (int)arguments[3]!;
        return parsed;
    }
    private static GeneratedNameKind ToKind(object roslynKind) {
        var name = Enum.GetName(kindType, roslynKind);
        return name != null && Enum.TryParse<GeneratedNameKind>(name, out var kind) ? kind : GeneratedNameKind.Other;
    }
}
