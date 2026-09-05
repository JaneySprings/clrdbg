// Roslyn's DkmUtilities is excluded from the build (it is written against the Dkm contract classes), but its
// 'GetResultProperties' is what the C# compilation context reports a compiled expression with. This is that method
// and its two helpers, copied verbatim; re-check them against DkmUtilities.cs after a Roslyn update
using Microsoft.CodeAnalysis.Symbols;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Evaluation.ClrCompilation;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.ExpressionEvaluator;

internal static class ResultPropertiesHelper {
    internal static ResultProperties GetResultProperties<TSymbol>(this TSymbol? symbol, DkmClrCompilationResultFlags flags, bool isConstant)
        where TSymbol : class, ISymbolInternal
    {
        var category = (symbol != null) ? GetResultCategory(symbol.Kind)
            : DkmEvaluationResultCategory.Data;

        var accessType = (symbol != null) ? GetResultAccessType(symbol.DeclaredAccessibility)
            : DkmEvaluationResultAccessType.None;

        var storageType = (symbol != null) && symbol.IsStatic
            ? DkmEvaluationResultStorageType.Static
            : DkmEvaluationResultStorageType.None;

        var modifierFlags = DkmEvaluationResultTypeModifierFlags.None;
        if (isConstant)
        {
            modifierFlags = DkmEvaluationResultTypeModifierFlags.Constant;
        }
        else if (symbol is null)
        {
            // No change.
        }
        else if (symbol.IsVirtual || symbol.IsAbstract || symbol.IsOverride)
        {
            modifierFlags = DkmEvaluationResultTypeModifierFlags.Virtual;
        }
        else if (symbol.Kind == SymbolKind.Field && ((IFieldSymbolInternal)symbol).IsVolatile)
        {
            modifierFlags = DkmEvaluationResultTypeModifierFlags.Volatile;
        }

        // CONSIDER: for completeness, we could check for [MethodImpl(MethodImplOptions.Synchronized)]
        // and set DkmEvaluationResultTypeModifierFlags.Synchronized, but it doesn't seem to have any
        // impact on the UI.  It is exposed through the DTE, but cscompee didn't set the flag either.

        return new ResultProperties(flags, category, accessType, storageType, modifierFlags);
    }

    private static DkmEvaluationResultCategory GetResultCategory(SymbolKind kind)
    {
        switch (kind)
        {
            case SymbolKind.Method:
                return DkmEvaluationResultCategory.Method;
            case SymbolKind.Property:
                return DkmEvaluationResultCategory.Property;
            default:
                return DkmEvaluationResultCategory.Data;
        }
    }

    private static DkmEvaluationResultAccessType GetResultAccessType(Accessibility accessibility)
    {
        switch (accessibility)
        {
            case Accessibility.Public:
                return DkmEvaluationResultAccessType.Public;
            case Accessibility.Protected:
                return DkmEvaluationResultAccessType.Protected;
            case Accessibility.Private:
                return DkmEvaluationResultAccessType.Private;
            case Accessibility.Internal:
            case Accessibility.ProtectedOrInternal: // Dev12 treats this as "internal"
            case Accessibility.ProtectedAndInternal: // Dev12 treats this as "internal"
                return DkmEvaluationResultAccessType.Internal;
            case Accessibility.NotApplicable:
                return DkmEvaluationResultAccessType.None;
            default:
                throw ExceptionUtilities.UnexpectedValue(accessibility);
        }
    }
}
