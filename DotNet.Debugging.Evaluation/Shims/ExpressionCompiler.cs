// Stand-in for Roslyn's ExpressionCompiler, the Dkm-facing base class excluded from the build: the vendored
// MetadataUtilities locates the embedded WindowsProxy.winmd resource through 'typeof(ExpressionCompiler).Assembly',
// which this keeps resolving to the assembly the resource is embedded in
namespace Microsoft.CodeAnalysis.ExpressionEvaluator;

internal abstract class ExpressionCompiler {
}
