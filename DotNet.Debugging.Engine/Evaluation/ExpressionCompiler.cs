using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Models;
using DotNet.Debugging.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNet.Debugging.Engine.Evaluation;

// Compiles C# expressions with the Roslyn expression compiler (DotNet.Debugging.Evaluation) against the metadata of
// the loaded modules
internal class ExpressionCompiler {
    private const int CacheCapacity = 256;

    private readonly ManagedDebugger debugger;
    private readonly Dictionary<string, CacheEntry> compileCache = new Dictionary<string, CacheEntry>();
    private readonly LinkedList<string> compileOrder = new LinkedList<string>();
    private readonly Dictionary<ModuleInfo, EvaluationMetadata> metadataCache = new Dictionary<ModuleInfo, EvaluationMetadata>();
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
            if (frame == null || entry.Constraints!.AreSatisfied(preferredModule.MetadataReader.Mvid, preferredModule.Name, frame.GetFunction().GetToken(), GetILOffset(frame))) {
                compileOrder.Remove(entry.Node);
                compileOrder.AddFirst(entry.Node);
                return entry.Value;
            }
            compileOrder.Remove(entry.Node);
            compileCache.Remove(cacheKey);
            entry.Value.Dispose();
        }

        var metadata = GetMetadata(preferredModule);
        var expressionContext = context.RootValue == null
            ? CreateMethodContext(metadata, frame!, preferredModule)
            : CreateTypeContext(metadata, context.RootValue, preferredModule);
        var result = expressionContext.Compile(expression, hasException);
        if (result.Assembly == null) {
            var message = string.Join("; ", result.Errors);
            if (result.MissingAssemblies.Count > 0)
                throw new MissingAssembliesException(message, result.MissingAssemblies);
            throw new EvaluationException(message);
        }
        var compiled = new CompiledExpression(result.Assembly, result.TypeName!, result.MethodName!);
        return AddToCache(cacheKey, compiled, expressionContext.ReuseConstraints);
    }

    private static string CreateCacheKey(string expression, EvaluationContext context, ICorDebugILFrame? frame, ModuleInfo preferredModule, bool hasException) {
        if (context.RootValue != null)
            return $"type|{preferredModule.Id}|{context.RootValue.GetExactType().GetClass().GetToken()}|{hasException}|{expression}";
        return $"method|{preferredModule.Id}|{frame!.GetFunction().GetToken()}|{hasException}|{expression}";
    }
    private static int GetILOffset(ICorDebugILFrame frame) {
        return ExpressionContext.NormalizeILOffset((uint)frame.GetIP().pnOffset);
    }
    // 'constraints' are the ones of a method context, null for a type one
    private CompiledExpression AddToCache(string key, CompiledExpression compiled, ReuseConstraints? constraints) {
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
        metadataCache.Clear();
    }

    // The metadata passed to the Roslyn evaluator, addressed in the readers' own storage (the runtime's importer
    // of a dynamic module is replaced as the module grows). When several loaded modules share an assembly identity
    // (the same assembly in several AssemblyLoadContexts) only one per identity is kept so Roslyn binds types
    // against a single instance - the module the evaluation binds against is preferred, as the tokens emitted
    // into the evaluation assembly must match the instance the user is debugging
    private EvaluationMetadata GetMetadata(ModuleInfo preferredModule) {
        if (metadataCache.TryGetValue(preferredModule, out var cached))
            return cached;

        var modules = GetModulesPreferring(preferredModule);
        var blocks = new List<ModuleMetadataBlock>(modules.Count);
        foreach (var moduleInfo in modules) {
            var (pointer, size) = moduleInfo.MetadataReader.GetMetadataStorage();
            var reader = moduleInfo.MetadataReader.PeMetadataReader;
            var module = reader.GetModuleDefinition();
            var generationId = module.GenerationId.IsNil ? Guid.Empty : reader.GetGuid(module.GenerationId);
            blocks.Add(new ModuleMetadataBlock(moduleInfo.MetadataReader.Mvid, moduleInfo.Name, generationId, pointer, size));
        }

        var metadata = new EvaluationMetadata(blocks);
        metadataCache[preferredModule] = metadata;
        return metadata;
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

    // The frame's method with the portable PDB read directly through the module's metadata reader
    private static ExpressionContext CreateMethodContext(EvaluationMetadata metadata, ICorDebugILFrame frame, ModuleInfo moduleInfo) {
        var function = frame.GetFunction();
        return ExpressionContext.CreateMethodContext(metadata, moduleInfo.MetadataReader.Mvid, moduleInfo.Name, function.GetToken(), function.GetLocalVarSigToken(), GetILOffset(frame), moduleInfo.MetadataReader.PdbMetadataReader);
    }
    private static ExpressionContext CreateTypeContext(EvaluationMetadata metadata, ICorDebugValue rootValue, ModuleInfo moduleInfo) {
        return ExpressionContext.CreateTypeContext(metadata, moduleInfo.MetadataReader.Mvid, moduleInfo.Name, rootValue.GetExactType().GetClass().GetToken());
    }

    private class CacheEntry {
        public CompiledExpression Value { get; }
        public LinkedListNode<string> Node { get; }
        // The method and IL span the expression was compiled for, null for a type (DebuggerDisplay) context
        public ReuseConstraints? Constraints { get; }

        public CacheEntry(CompiledExpression value, LinkedListNode<string> node, ReuseConstraints? constraints) {
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
