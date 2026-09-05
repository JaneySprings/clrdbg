using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.ExpressionEvaluator;

namespace DotNet.Debugging.Evaluation;

// The metadata of the loaded modules an expression is compiled against, built once per module set
public class EvaluationMetadata {
    internal ImmutableArray<MetadataBlock> Blocks { get; }

    public EvaluationMetadata(IReadOnlyList<ModuleMetadataBlock> modules) {
        var blocks = ImmutableArray.CreateBuilder<MetadataBlock>(modules.Count);
        foreach (var module in modules)
            blocks.Add(new MetadataBlock(new ModuleId(module.Mvid, module.Name), module.GenerationId, module.Pointer, module.Size));
        Blocks = blocks.MoveToImmutable();
    }
}
