using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE;
using Microsoft.CodeAnalysis.ExpressionEvaluator;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.VisualStudio.Debugger.Clr;
using Microsoft.VisualStudio.Debugger.Evaluation;

namespace DotNet.Debugging.Evaluation;

// The Roslyn C# expression compiler bound to a frame's method (its locals, the hoisted locals in scope at the IL
// offset, the PDB's imports and constants) or to a type (a DebuggerDisplay expression evaluated against a value alone)
public class ExpressionContext {
    // The debuggee's methods are never edited, Roslyn numbers method versions from 1
    internal const int MethodVersion = 1;

    private readonly EvaluationContext context;

    // The method and IL span the context is valid for, null for a type context
    public ReuseConstraints? ReuseConstraints { get; }

    private ExpressionContext(EvaluationContext context) {
        this.context = context;
        if (context.MethodContextReuseConstraints != null)
            ReuseConstraints = new ReuseConstraints(context.MethodContextReuseConstraints.Value);
    }

    // What Roslyn's own EvaluationContext.CreateMethodContext does, with the portable PDB read through its metadata
    // reader rather than through a symbol reader; 'ilOffset' is normalized, 'pdbReader' null when the module has no symbols
    public static ExpressionContext CreateMethodContext(EvaluationMetadata metadata, Guid mvid, string moduleName, int methodToken, int localSignatureToken, int ilOffset, MetadataReader? pdbReader) {
        var moduleId = new ModuleId(mvid, moduleName);
        var compilation = CreateCompilation(metadata);
        var methodHandle = (MethodDefinitionHandle)MetadataTokens.Handle(methodToken);
        var currentSourceMethod = compilation.GetSourceMethod(moduleId, methodHandle);
        var currentFrame = compilation.GetMethod(moduleId, methodHandle);
        var containingModule = (PEModuleSymbol)currentFrame.ContainingModule;
        var symbolProvider = new CSharpEESymbolProvider(compilation.SourceAssembly, containingModule, currentFrame);
        var metadataDecoder = new MetadataDecoder(containingModule, currentFrame);
        var localSignature = localSignatureToken == 0 ? default : (StandaloneSignatureHandle)MetadataTokens.Handle(localSignatureToken);
        var localInfo = metadataDecoder.GetLocalInfo(localSignature);
        var debugInfo = pdbReader == null
            ? MethodDebugInfo<TypeSymbol, LocalSymbol>.None
            : MethodDebugInfo<TypeSymbol, LocalSymbol>.ReadFromPortable(pdbReader, methodToken, ilOffset, symbolProvider, isVisualBasicMethod: false);
        var reuseSpan = debugInfo.ReuseSpan;
        var locals = ArrayBuilder<LocalSymbol>.GetInstance();
        MethodDebugInfo<TypeSymbol, LocalSymbol>.GetLocals(locals, symbolProvider, debugInfo.LocalVariableNames, localInfo, debugInfo.DynamicLocalMap, debugInfo.TupleLocalMap);
        var inScopeHoistedLocals = debugInfo.GetInScopeHoistedLocalIndices(ilOffset, ref reuseSpan);
        locals.AddRange(debugInfo.LocalConstants);
        var evaluationContext = CreateEvaluationContext(
            new MethodContextReuseConstraints(moduleId, methodToken, MethodVersion, reuseSpan),
            compilation,
            currentFrame,
            currentSourceMethod,
            locals.ToImmutableAndFree(),
            inScopeHoistedLocals,
            debugInfo);
        return new ExpressionContext(evaluationContext);
    }
    // A synthesized method on the type, with the displayed object as its only argument
    public static ExpressionContext CreateTypeContext(EvaluationMetadata metadata, Guid mvid, string moduleName, int typeToken) {
        var compilation = CreateCompilation(metadata);
        return new ExpressionContext(EvaluationContext.CreateTypeContext(compilation, new ModuleId(mvid, moduleName), typeToken));
    }
    // The special offsets of a prolog or epilog (0xffffffff and friends) are mapped to offset 0
    public static int NormalizeILOffset(uint ilOffset) {
        return EvaluationContextBase.NormalizeILOffset(ilOffset);
    }

    // 'hasException' registers the '$exception' pseudo variable the expression may name
    public ExpressionCompileResult Compile(string expression, bool hasException) {
        var diagnostics = DiagnosticBag.GetInstance();
        try {
            var result = context.CompileExpression(expression, DkmEvaluationFlags.TreatAsExpression, GetAliases(hasException), diagnostics, out _, testData: null);
            if (result == null || diagnostics.HasAnyErrors()) {
                var errors = diagnostics.AsEnumerable().Where(it => it.Severity == DiagnosticSeverity.Error).ToList();
                return new ExpressionCompileResult(errors.Select(it => it.GetMessage()).ToList(), GetMissingAssemblies(errors));
            }
            return new ExpressionCompileResult(result.Assembly, result.TypeName, result.MethodName);
        }
        finally {
            diagnostics.Free();
        }
    }

    // The assemblies the first error blaming any names, the way Roslyn's own retry loop finds them: an unknown type's
    // assembly, or System.Linq for an extension method that could be one of its
    private IReadOnlyList<string> GetMissingAssemblies(List<Diagnostic> errors) {
        foreach (var error in errors) {
            var identities = context.GetMissingAssemblyIdentities(error, EvaluationContextBase.SystemLinqIdentity);
            if (!identities.IsDefaultOrEmpty)
                return identities.Select(it => it.Name).ToList();
        }
        return Array.Empty<string>();
    }
    // A compilation referencing every block ('AllAssemblies'), the way the expression compiler builds one when it does
    // not know which module the expression will bind against, plus the intrinsics it emits calls to
    private static CSharpCompilation CreateCompilation(EvaluationMetadata metadata) {
        return metadata.Blocks.ToCompilation(default(ModuleId), MakeAssemblyReferencesKind.AllAssemblies).AddReferences(IntrinsicMethodsReference.Instance);
    }
    // Roslyn only creates contexts from a symbol reader, its constructor is private: the accessor binds to it without
    // an edit to the vendored file and fails at first use, naming it, should a Roslyn update change its signature
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern EvaluationContext CreateEvaluationContext(MethodContextReuseConstraints? methodContextReuseConstraints, CSharpCompilation compilation, MethodSymbol currentFrame, MethodSymbol? currentSourceMethod, ImmutableArray<LocalSymbol> locals, ImmutableSortedSet<int> inScopeHoistedLocalSlots, MethodDebugInfo<TypeSymbol, LocalSymbol> methodDebugInfo);
    private static ImmutableArray<Alias> GetAliases(bool hasException) {
        if (!hasException)
            return ImmutableArray<Alias>.Empty;
        return ImmutableArray.Create(new Alias(DkmClrAliasKind.Exception, "Error", "$exception", typeof(Exception).AssemblyQualifiedName!, Guid.Empty, null!));
    }
}
