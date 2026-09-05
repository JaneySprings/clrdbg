using Microsoft.CodeAnalysis.CSharp.Symbols;
using RoslynGeneratedNameKind = Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameKind;

namespace DotNet.Debugging.Evaluation;

// Reads the kind out of a compiler generated name ('<>4__this', '<count>5__1', '<>c__DisplayClass0_0') with the
// C# compiler's own parser
public static class GeneratedNames {
    public static GeneratedNameKind GetKind(string name) {
        return ToKind(GeneratedNameParser.GetKind(name));
    }
    // The offsets delimit the original name inside the generated one ('<count>5__1' -> 'count')
    public static bool TryParseGeneratedName(string name, out GeneratedNameKind kind, out int openBracketOffset, out int closeBracketOffset) {
        var parsed = GeneratedNameParser.TryParseGeneratedName(name, out var roslynKind, out openBracketOffset, out closeBracketOffset);
        kind = ToKind(roslynKind);
        return parsed;
    }

    private static GeneratedNameKind ToKind(RoslynGeneratedNameKind kind) {
        switch (kind) {
            case RoslynGeneratedNameKind.None: return GeneratedNameKind.None;
            case RoslynGeneratedNameKind.ThisProxyField: return GeneratedNameKind.ThisProxyField;
            case RoslynGeneratedNameKind.HoistedLocalField: return GeneratedNameKind.HoistedLocalField;
            case RoslynGeneratedNameKind.DisplayClassLocalOrField: return GeneratedNameKind.DisplayClassLocalOrField;
            case RoslynGeneratedNameKind.LambdaDisplayClass: return GeneratedNameKind.LambdaDisplayClass;
            case RoslynGeneratedNameKind.StateMachineType: return GeneratedNameKind.StateMachineType;
            default: return GeneratedNameKind.Other;
        }
    }
}
