using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.ExpressionEvaluator.MethodDebugInfo<TypeSymbol, LocalSymbol>: what the PDB says about a
// method at an IL offset - local names, hoisted locals in scope, constants, imports, the reuse span
internal static class InternalMethodDebugInfo {
    public static Type Type { get; }
    private static readonly MethodInfo readFromPortableMethod;
    private static readonly MethodInfo getLocalsMethod;
    private static readonly MethodInfo getInScopeHoistedLocalIndicesMethod;
    private static readonly FieldInfo reuseSpanField;
    private static readonly FieldInfo localVariableNamesField;
    private static readonly FieldInfo dynamicLocalMapField;
    private static readonly FieldInfo tupleLocalMapField;
    private static readonly FieldInfo localConstantsField;

    // The debug information of a method without symbols
    public static object None { get; }

    static InternalMethodDebugInfo() {
        var openType = RoslynReflection.GetType(RoslynReflection.ExpressionCompilerAssembly, "Microsoft.CodeAnalysis.ExpressionEvaluator.MethodDebugInfo`2");
        Type = openType.MakeGenericType(InternalLocalSymbol.TypeSymbolType, InternalLocalSymbol.Type);
        readFromPortableMethod = RoslynReflection.GetMethod(Type, "ReadFromPortable", 5);
        getLocalsMethod = RoslynReflection.GetMethod(Type, "GetLocals", 6);
        getInScopeHoistedLocalIndicesMethod = RoslynReflection.GetMethod(Type, "GetInScopeHoistedLocalIndices", 2);
        reuseSpanField = RoslynReflection.GetField(Type, "ReuseSpan");
        localVariableNamesField = RoslynReflection.GetField(Type, "LocalVariableNames");
        dynamicLocalMapField = RoslynReflection.GetField(Type, "DynamicLocalMap");
        tupleLocalMapField = RoslynReflection.GetField(Type, "TupleLocalMap");
        localConstantsField = RoslynReflection.GetField(Type, "LocalConstants");
        None = RoslynReflection.GetField(Type, "None").GetValue(null)!;
    }

    // Reads the portable PDB's custom debug information of the method around the offset
    public static object ReadFromPortable(MetadataReader pdbReader, int methodToken, int ilOffset, object symbolProvider) {
        return readFromPortableMethod.InvokeUnwrapped(null, [pdbReader, methodToken, ilOffset, symbolProvider, false])!;
    }
    // Adds the method's locals (named by the PDB, typed by the local signature) to an ArrayBuilder<LocalSymbol>
    public static void GetLocals(object localsBuilder, object symbolProvider, object debugInfo, object localInfo) {
        getLocalsMethod.InvokeUnwrapped(null, [localsBuilder, symbolProvider, localVariableNamesField.GetValue(debugInfo), localInfo, dynamicLocalMapField.GetValue(debugInfo), tupleLocalMapField.GetValue(debugInfo)]);
    }
    // The slots of the hoisted locals in scope at the offset; the reuse span is narrowed to the scopes consulted
    public static ImmutableSortedSet<int> GetInScopeHoistedLocalIndices(object debugInfo, int ilOffset, ref object reuseSpan) {
        var arguments = new object?[] { ilOffset, reuseSpan };
        var result = (ImmutableSortedSet<int>)getInScopeHoistedLocalIndicesMethod.InvokeUnwrapped(debugInfo, arguments)!;
        reuseSpan = arguments[1]!;
        return result;
    }
    // The ILSpan the compiled context stays valid for
    public static object GetReuseSpan(object debugInfo) {
        return reuseSpanField.GetValue(debugInfo)!;
    }
    // The ImmutableArray<LocalSymbol> of the constants in scope
    public static object GetLocalConstants(object debugInfo) {
        return localConstantsField.GetValue(debugInfo)!;
    }
}
