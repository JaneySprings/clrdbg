using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.ExpressionEvaluator;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.VisualStudio.Debugger.Clr;
using Microsoft.VisualStudio.Debugger.Evaluation;

namespace DotNet.Debugging.Engine.Evaluation;

// Compiles C# expressions with the Roslyn expression compiler against the metadata of the loaded modules
internal class ExpressionCompiler {
    private const int CacheCapacity = 256;
    private static readonly PortableExecutableReference intrinsicMethodsReference = MetadataReference.CreateFromImage(CreateIntrinsicMethodsAssembly());

    private readonly ManagedDebugger debugger;
    private readonly Dictionary<string, CacheEntry> compileCache = new Dictionary<string, CacheEntry>();
    private readonly LinkedList<string> compileOrder = new LinkedList<string>();
    private readonly Dictionary<ModuleInfo, ImmutableArray<MetadataBlock>> metadataBlocksCache = new Dictionary<ModuleInfo, ImmutableArray<MetadataBlock>>();
    private int cachedModulesVersion = -1;

    public ExpressionCompiler(ManagedDebugger debugger) {
        this.debugger = debugger;
    }

    public CompiledExpression Compile(string expression, EvaluationContext context) {
        // A type context (DebuggerDisplay) evaluation binds against the value alone, no IL frame is needed for it
        ICorDebugILFrame? frame = null;
        ModuleInfo preferredModule;
        if (context.RootValue != null) {
            // DebuggerDisplay format specifiers ({Name,nq}) are not valid interpolation alignments
            var syntax = SyntaxFactory.ParseExpression(expression);
            expression = new RemoveFormatSpecifierRewriter().Visit(syntax)!.ToFullString();
            preferredModule = debugger.GetModule(context.RootValue.GetExactType().GetClass().GetModule());
        }
        else {
            frame = debugger.GetILFrame(context.ThreadId, context.FrameDepth);
            preferredModule = debugger.GetModule(frame.GetFunction().GetModule());
        }
        var hasException = debugger.GetCurrentException(context.ThreadId) != null;

        InvalidateCacheOnModuleChange();
        var cacheKey = CreateCacheKey(expression, context, frame, preferredModule, hasException);
        if (compileCache.TryGetValue(cacheKey, out var entry)) {
            compileOrder.Remove(entry.Node);
            compileOrder.AddFirst(entry.Node);
            return entry.Value;
        }

        var blocks = GetMetadataBlocks(preferredModule);
        var evaluationContext = context.RootValue == null
            ? CreateMethodContext(blocks, frame!, preferredModule)
            : CreateTypeContext(blocks, context.RootValue, preferredModule);

        var diagnostics = DiagnosticBag.GetInstance();
        try {
            var result = evaluationContext.CompileExpression(expression, DkmEvaluationFlags.TreatAsExpression, GetAliases(hasException), diagnostics, out _, testData: null);
            if (result == null || diagnostics.HasAnyErrors()) {
                var errors = diagnostics.AsEnumerable().Where(it => it.Severity == DiagnosticSeverity.Error).Select(it => it.GetMessage());
                throw new EvaluationException(string.Join("; ", errors));
            }
            return AddToCache(cacheKey, new CompiledExpression(result.Assembly, result.TypeName, result.MethodName));
        }
        finally {
            diagnostics.Free();
        }
    }

    private static string CreateCacheKey(string expression, EvaluationContext context, ICorDebugILFrame? frame, ModuleInfo preferredModule, bool hasException) {
        if (context.RootValue != null)
            return $"type|{preferredModule.Id}|{context.RootValue.GetExactType().GetClass().GetToken()}|{hasException}|{expression}";

        var ilOffset = EvaluationContextBase.NormalizeILOffset((uint)frame!.GetIP().pnOffset);
        return $"method|{preferredModule.Id}|{frame!.GetFunction().GetToken()}|{ilOffset}|{hasException}|{expression}";
    }
    private CompiledExpression AddToCache(string key, CompiledExpression compiled) {
        if (compileCache.Count >= CacheCapacity) {
            var oldestKey = compileOrder.Last!.Value;
            compileOrder.RemoveLast();
            if (compileCache.Remove(oldestKey, out var oldest))
                oldest.Value.Dispose();
        }
        compileCache[key] = new CacheEntry(compiled, compileOrder.AddFirst(key));
        return compiled;
    }
    // Everything compiled is bound to the module set, a newly loaded module changes what an expression resolves to
    private void InvalidateCacheOnModuleChange() {
        if (debugger.ModulesVersion == cachedModulesVersion)
            return;
        cachedModulesVersion = debugger.ModulesVersion;
        foreach (var entry in compileCache.Values)
            entry.Value.Dispose();
        compileCache.Clear();
        compileOrder.Clear();
        metadataBlocksCache.Clear();
    }
    private static ImmutableArray<Alias> GetAliases(bool hasException) {
        if (!hasException)
            return ImmutableArray<Alias>.Empty;
        return [new Alias(DkmClrAliasKind.Exception, "Error", "$exception", typeof(Exception).AssemblyQualifiedName!, Guid.Empty, null!)];
    }

    // The metadata passed to the Roslyn evaluator, addressed in the readers' own storage (the runtime's importer
    // of a dynamic module is replaced as the module grows). When several loaded modules share an assembly identity
    // (the same assembly in several AssemblyLoadContexts) only one per identity is kept so Roslyn binds types
    // against a single instance - the module the evaluation binds against is preferred, as the tokens emitted
    // into the evaluation assembly must match the instance the user is debugging
    private ImmutableArray<MetadataBlock> GetMetadataBlocks(ModuleInfo preferredModule) {
        if (metadataBlocksCache.TryGetValue(preferredModule, out var cached))
            return cached;

        var modules = GetModulesPreferring(preferredModule);
        var builder = ImmutableArray.CreateBuilder<MetadataBlock>(modules.Count);
        foreach (var moduleInfo in modules) {
            var (pointer, size) = moduleInfo.MetadataReader.GetMetadataStorage();
            var reader = moduleInfo.MetadataReader.PeMetadataReader;
            var module = reader.GetModuleDefinition();
            var generationId = module.GenerationId.IsNil ? Guid.Empty : reader.GetGuid(module.GenerationId);
            builder.Add(new MetadataBlock(new ModuleId(moduleInfo.MetadataReader.Mvid, moduleInfo.Name), generationId, pointer, size));
        }

        var blocks = builder.ToImmutable();
        metadataBlocksCache[preferredModule] = blocks;
        return blocks;
    }
    private List<ModuleInfo> GetModulesPreferring(ModuleInfo preferredModule) {
        var modulesByIdentity = new Dictionary<string, List<ModuleInfo>>(StringComparer.Ordinal);
        foreach (var moduleInfo in debugger.Modules) {
            var identity = GetAssemblyIdentity(moduleInfo);
            if (!modulesByIdentity.TryGetValue(identity, out var modules)) {
                modules = new List<ModuleInfo>();
                modulesByIdentity[identity] = modules;
            }
            modules.Add(moduleInfo);
        }

        var result = new List<ModuleInfo>(modulesByIdentity.Count);
        foreach (var modules in modulesByIdentity.Values)
            result.Add(modules.FirstOrDefault(it => it == preferredModule) ?? modules[0]);
        return result;
    }
    private static string GetAssemblyIdentity(ModuleInfo moduleInfo) {
        var reader = moduleInfo.MetadataReader.PeMetadataReader;
        if (!reader.IsAssembly)
            return $"{moduleInfo.Name}|{moduleInfo.MetadataReader.Mvid}";

        var assembly = reader.GetAssemblyDefinition();
        var culture = reader.GetString(assembly.Culture);
        var publicKey = assembly.PublicKey.IsNil ? string.Empty : Convert.ToHexString(reader.GetBlobBytes(assembly.PublicKey));
        return $"{reader.GetString(assembly.Name)}|{assembly.Version}|{culture}|{publicKey}";
    }

    private static Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.EvaluationContext CreateMethodContext(ImmutableArray<MetadataBlock> blocks, ICorDebugILFrame frame, ModuleInfo moduleInfo) {
        var function = frame.GetFunction();
        var moduleId = new ModuleId(moduleInfo.MetadataReader.Mvid, moduleInfo.Name);
        var compilation = blocks.ToCompilation(moduleId: default, MakeAssemblyReferencesKind.AllAssemblies).AddReferences(intrinsicMethodsReference);
        var methodToken = function.GetToken();
        var methodHandle = (MethodDefinitionHandle)MetadataTokens.Handle(methodToken);
        var currentSourceMethod = compilation.GetSourceMethod(moduleId, methodHandle);
        var currentFrame = compilation.GetMethod(moduleId, methodHandle);
        var symbolProvider = new CSharpEESymbolProvider(compilation.SourceAssembly, (PEModuleSymbol)currentFrame.ContainingModule, currentFrame);
        var metadataDecoder = new MetadataDecoder((PEModuleSymbol)currentFrame.ContainingModule, currentFrame);
        var localSignatureToken = function.GetLocalVarSigToken();
        var localSignature = localSignatureToken == 0 ? default : (StandaloneSignatureHandle)MetadataTokens.Handle(localSignatureToken);
        var localInfo = metadataDecoder.GetLocalInfo(localSignature);
        var ilOffset = EvaluationContextBase.NormalizeILOffset((uint)frame.GetIP().pnOffset);
        var pdbReader = moduleInfo.MetadataReader.PdbMetadataReader;
        var debugInfo = pdbReader == null
            ? MethodDebugInfo<TypeSymbol, LocalSymbol>.None
            : MethodDebugInfo<TypeSymbol, LocalSymbol>.ReadFromPortable(pdbReader, methodToken, ilOffset, symbolProvider, isVisualBasicMethod: false);
        var reuseSpan = debugInfo.ReuseSpan;
        var locals = ArrayBuilder<LocalSymbol>.GetInstance();
        MethodDebugInfo<TypeSymbol, LocalSymbol>.GetLocals(locals, symbolProvider, debugInfo.LocalVariableNames, localInfo, debugInfo.DynamicLocalMap, debugInfo.TupleLocalMap);
        var hoistedLocals = debugInfo.GetInScopeHoistedLocalIndices(ilOffset, ref reuseSpan);
        locals.AddRange(debugInfo.LocalConstants);

        return new Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.EvaluationContext(
            new MethodContextReuseConstraints(moduleId, methodToken, methodVersion: 1, reuseSpan),
            compilation,
            currentFrame,
            currentSourceMethod,
            locals.ToImmutableAndFree(),
            hoistedLocals,
            debugInfo);
    }
    private static Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.EvaluationContext CreateTypeContext(ImmutableArray<MetadataBlock> blocks, ICorDebugValue rootValue, ModuleInfo moduleInfo) {
        var rootType = rootValue.GetExactType();
        var moduleId = new ModuleId(moduleInfo.MetadataReader.Mvid, moduleInfo.Name);
        var compilation = blocks.ToCompilation(moduleId: default, MakeAssemblyReferencesKind.AllAssemblies).AddReferences(intrinsicMethodsReference);
        var currentType = compilation.GetType(moduleId, rootType.GetClass().GetToken());
        return new Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.EvaluationContext(
            null,
            compilation,
            new SynthesizedContextMethodSymbol(currentType),
            currentSourceMethod: null,
            locals: default,
            inScopeHoistedLocalSlots: ImmutableSortedSet<int>.Empty,
            methodDebugInfo: MethodDebugInfo<TypeSymbol, LocalSymbol>.None);
    }

    // The debugger intrinsics the Roslyn evaluator emits calls to, handled by the interpreter
    private static ImmutableArray<byte> CreateIntrinsicMethodsAssembly() {
        const string source = """
            namespace Microsoft.VisualStudio.Debugger.Clr;

            public static class IntrinsicMethods
            {
                public static void CreateVariable(System.Type type, string name, System.Guid customTypeInfoPayloadTypeId, byte[] customTypeInfoPayload) { }
                public static object GetObjectByAlias(string name) => throw null;
                public static ref T GetVariableAddress<T>(string name) => throw null;
                public static System.Exception GetException() => throw null;
            }
            """;
        var compilation = CSharpCompilation.Create(
            "DotNet.Debugging.Intrinsics",
            [SyntaxFactory.ParseSyntaxTree(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(it => it.GetMessage())));
        return ImmutableArray.Create(stream.ToArray());
    }

    private class CacheEntry {
        public CompiledExpression Value { get; }
        public LinkedListNode<string> Node { get; }

        public CacheEntry(CompiledExpression value, LinkedListNode<string> node) {
            Value = value;
            Node = node;
        }
    }

    private class RemoveFormatSpecifierRewriter : CSharpSyntaxRewriter {
        public override SyntaxNode? VisitInterpolation(InterpolationSyntax node) {
            var result = (InterpolationSyntax)base.VisitInterpolation(node)!;
            if (result.AlignmentClause != null && result.AlignmentClause.Value is not LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NumericLiteralExpression })
                result = result.WithAlignmentClause(null);
            return result;
        }
    }
}
