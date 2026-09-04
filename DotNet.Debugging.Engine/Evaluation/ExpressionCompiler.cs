using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Models;
using DotNet.Debugging.Engine.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Debugger.Clr;
using Microsoft.VisualStudio.Debugger.Evaluation;

namespace DotNet.Debugging.Engine.Evaluation;

// Compiles C# expressions with the Roslyn expression compiler against the metadata of the loaded modules. The
// compiler is internal to Roslyn, it is driven through the wrappers of 'Reflection/'; the Roslyn objects passed
// between them (metadata blocks, evaluation contexts, symbols) are held as plain objects here
internal class ExpressionCompiler {
    private const int CacheCapacity = 256;
    private static readonly PortableExecutableReference intrinsicMethodsReference = MetadataReference.CreateFromImage(CreateIntrinsicMethodsAssembly());

    private readonly ManagedDebugger debugger;
    private readonly Dictionary<string, CacheEntry> compileCache = new Dictionary<string, CacheEntry>();
    private readonly LinkedList<string> compileOrder = new LinkedList<string>();
    // The ImmutableArray<MetadataBlock> per preferred module
    private readonly Dictionary<ModuleInfo, object> metadataBlocksCache = new Dictionary<ModuleInfo, object>();
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
            // A method context serves every IL offset of the span it was compiled for, the same locals are in scope there
            if (frame == null || InternalMethodContextReuseConstraints.AreSatisfied(entry.Constraints!, CreateModuleId(preferredModule), frame.GetFunction().GetToken(), 1, GetILOffset(frame))) {
                compileOrder.Remove(entry.Node);
                compileOrder.AddFirst(entry.Node);
                return entry.Value;
            }
            compileOrder.Remove(entry.Node);
            compileCache.Remove(cacheKey);
            entry.Value.Dispose();
        }

        var blocks = GetMetadataBlocks(preferredModule);
        var evaluationContext = context.RootValue == null
            ? CreateMethodContext(blocks, frame!, preferredModule)
            : CreateTypeContext(blocks, context.RootValue, preferredModule);

        var diagnostics = InternalDiagnosticBag.GetInstance();
        try {
            var result = InternalEvaluationContext.CompileExpression(evaluationContext, expression, DkmEvaluationFlags.TreatAsExpression, GetAliases(hasException), diagnostics);
            if (result == null || InternalDiagnosticBag.HasAnyErrors(diagnostics)) {
                var errors = InternalDiagnosticBag.AsEnumerable(diagnostics).Where(it => it.Severity == DiagnosticSeverity.Error).Select(it => it.GetMessage());
                throw new EvaluationException(string.Join("; ", errors));
            }
            var compiled = new CompiledExpression(InternalCompileResult.GetAssembly(result), InternalCompileResult.GetTypeName(result), InternalCompileResult.GetMethodName(result));
            return AddToCache(cacheKey, compiled, InternalEvaluationContext.GetMethodContextReuseConstraints(evaluationContext));
        }
        finally {
            InternalDiagnosticBag.Free(diagnostics);
        }
    }

    private static string CreateCacheKey(string expression, EvaluationContext context, ICorDebugILFrame? frame, ModuleInfo preferredModule, bool hasException) {
        if (context.RootValue != null)
            return $"type|{preferredModule.Id}|{context.RootValue.GetExactType().GetClass().GetToken()}|{hasException}|{expression}";
        return $"method|{preferredModule.Id}|{frame!.GetFunction().GetToken()}|{hasException}|{expression}";
    }
    private static int GetILOffset(ICorDebugILFrame frame) {
        return InternalEvaluationContextBase.NormalizeILOffset((uint)frame.GetIP().pnOffset);
    }
    private static object CreateModuleId(ModuleInfo moduleInfo) {
        return InternalModuleId.Create(moduleInfo.MetadataReader.Mvid, moduleInfo.Name);
    }
    // 'constraints' is the MethodContextReuseConstraints of a method context, null for a type one
    private CompiledExpression AddToCache(string key, CompiledExpression compiled, object? constraints) {
        if (compileCache.Count >= CacheCapacity) {
            var oldestKey = compileOrder.Last!.Value;
            compileOrder.RemoveLast();
            if (compileCache.Remove(oldestKey, out var oldest))
                oldest.Value.Dispose();
        }
        compileCache[key] = new CacheEntry(compiled, compileOrder.AddFirst(key), constraints);
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
    // The ImmutableArray<Alias> of the pseudo variables the expression may name
    private static object GetAliases(bool hasException) {
        if (!hasException)
            return InternalAlias.Empty;
        return InternalAlias.CreateArray([InternalAlias.Create(DkmClrAliasKind.Exception, "Error", "$exception", typeof(Exception).AssemblyQualifiedName!)]);
    }

    // The metadata passed to the Roslyn evaluator, addressed in the readers' own storage (the runtime's importer
    // of a dynamic module is replaced as the module grows). When several loaded modules share an assembly identity
    // (the same assembly in several AssemblyLoadContexts) only one per identity is kept so Roslyn binds types
    // against a single instance - the module the evaluation binds against is preferred, as the tokens emitted
    // into the evaluation assembly must match the instance the user is debugging
    private object GetMetadataBlocks(ModuleInfo preferredModule) {
        if (metadataBlocksCache.TryGetValue(preferredModule, out var cached))
            return cached;

        var modules = GetModulesPreferring(preferredModule);
        var blocks = new List<object>(modules.Count);
        foreach (var moduleInfo in modules) {
            var (pointer, size) = moduleInfo.MetadataReader.GetMetadataStorage();
            var reader = moduleInfo.MetadataReader.PeMetadataReader;
            var module = reader.GetModuleDefinition();
            var generationId = module.GenerationId.IsNil ? Guid.Empty : reader.GetGuid(module.GenerationId);
            blocks.Add(InternalMetadataBlock.Create(CreateModuleId(moduleInfo), generationId, pointer, size));
        }

        var blocksArray = InternalMetadataBlock.CreateArray(blocks);
        metadataBlocksCache[preferredModule] = blocksArray;
        return blocksArray;
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

    // What Roslyn's own EvaluationContext.CreateMethodContext does, with the portable PDB read directly through
    // its metadata reader rather than through a symbol reader. Returns the C# EvaluationContext
    private static object CreateMethodContext(object blocks, ICorDebugILFrame frame, ModuleInfo moduleInfo) {
        var function = frame.GetFunction();
        var moduleId = CreateModuleId(moduleInfo);
        var compilation = InternalCompilationExtensions.ToCompilation(blocks).AddReferences(intrinsicMethodsReference);
        var methodToken = function.GetToken();
        var methodHandle = (MethodDefinitionHandle)MetadataTokens.Handle(methodToken);
        var currentSourceMethod = InternalCompilationExtensions.GetSourceMethod(compilation, moduleId, methodHandle);
        var currentFrame = InternalCompilationExtensions.GetMethod(compilation, moduleId, methodHandle);
        var containingModule = InternalSymbol.GetContainingModule(currentFrame);
        var symbolProvider = InternalCSharpEESymbolProvider.Create(InternalCSharpCompilation.GetSourceAssembly(compilation), containingModule, currentFrame);
        var metadataDecoder = InternalMetadataDecoder.Create(containingModule, currentFrame);
        var localSignatureToken = function.GetLocalVarSigToken();
        var localSignature = localSignatureToken == 0 ? default : (StandaloneSignatureHandle)MetadataTokens.Handle(localSignatureToken);
        var localInfo = InternalMetadataDecoder.GetLocalInfo(metadataDecoder, localSignature);
        var ilOffset = GetILOffset(frame);
        var pdbReader = moduleInfo.MetadataReader.PdbMetadataReader;
        var debugInfo = pdbReader == null
            ? InternalMethodDebugInfo.None
            : InternalMethodDebugInfo.ReadFromPortable(pdbReader, methodToken, ilOffset, symbolProvider);
        var reuseSpan = InternalMethodDebugInfo.GetReuseSpan(debugInfo);
        var locals = InternalArrayBuilder.GetInstance();
        InternalMethodDebugInfo.GetLocals(locals, symbolProvider, debugInfo, localInfo);
        var hoistedLocals = InternalMethodDebugInfo.GetInScopeHoistedLocalIndices(debugInfo, ilOffset, ref reuseSpan);
        InternalArrayBuilder.AddRange(locals, InternalMethodDebugInfo.GetLocalConstants(debugInfo));

        return InternalEvaluationContext.Create(
            InternalMethodContextReuseConstraints.Create(moduleId, methodToken, methodVersion: 1, reuseSpan),
            compilation,
            currentFrame,
            currentSourceMethod,
            InternalArrayBuilder.ToImmutableAndFree(locals),
            hoistedLocals,
            debugInfo);
    }
    private static object CreateTypeContext(object blocks, ICorDebugValue rootValue, ModuleInfo moduleInfo) {
        var rootType = rootValue.GetExactType();
        var moduleId = CreateModuleId(moduleInfo);
        var compilation = InternalCompilationExtensions.ToCompilation(blocks).AddReferences(intrinsicMethodsReference);
        var currentType = InternalCompilationExtensions.GetType(compilation, moduleId, rootType.GetClass().GetToken());
        return InternalEvaluationContext.Create(
            null,
            compilation,
            InternalSynthesizedContextMethodSymbol.Create(currentType),
            currentSourceMethod: null,
            locals: InternalLocalSymbol.DefaultImmutableArray,
            inScopeHoistedLocalSlots: ImmutableSortedSet<int>.Empty,
            methodDebugInfo: InternalMethodDebugInfo.None);
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
        // The MethodContextReuseConstraints (method and IL span) the expression was compiled for, null for a type (DebuggerDisplay) context
        public object? Constraints { get; }

        public CacheEntry(CompiledExpression value, LinkedListNode<string> node, object? constraints) {
            Value = value;
            Node = node;
            Constraints = constraints;
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
