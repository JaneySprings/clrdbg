using System.Reflection.Metadata;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;

namespace DotNet.Debugging.Engine.Evaluation;

// The spans an expression builds live on the host: a byref-like struct has no place a func eval could pass it
// through, and the compiler lowers everyday C# to spans - a string concatenated with a char goes through
// 'string.Concat(ReadOnlySpan<char>, ReadOnlySpan<char>)', an array literal bound to 'Contains' or 'SequenceEqual'
// through 'RuntimeHelpers.CreateSpan' and 'MemoryExtensions'. The span members, the conversions creating spans and
// the span-taking helpers run here over the string, array or values behind the span
internal class SpanEmulator {
    private const string ReadOnlySpanType = "System.ReadOnlySpan`1";
    private const string SpanType = "System.Span`1";
    private const string MemoryExtensionsType = "System.MemoryExtensions";

    private static readonly ResolvedCilType CharType = ResolvedCilType.FromPrimitive(PrimitiveTypeCode.Char);

    private readonly Func<HostSequence, Task<CilValue>> createArray;
    private readonly Func<ResolvedCilType, CorElementType?> getElementType;
    private readonly Func<FieldDefinitionHandle, byte[]> getFieldData;

    public SpanEmulator(Func<HostSequence, Task<CilValue>> createArray, Func<ResolvedCilType, CorElementType?> getElementType, Func<FieldDefinitionHandle, byte[]> getFieldData) {
        this.createArray = createArray;
        this.getElementType = getElementType;
        this.getFieldData = getFieldData;
    }

    public static bool IsSpanType(string typeName) {
        return typeName == ReadOnlySpanType || typeName == SpanType;
    }
    // Whether the call is one the emulator serves: a span's own members, the conversions and helpers creating spans,
    // and the span-taking helpers once a span the expression built is among the arguments
    public static bool Handles(ResolvedRuntimeMethod method, CilValue[] arguments) {
        var typeName = method.DeclaringType.FullName;
        // Including Object's virtuals called through 'constrained.' on a span ('span.ToString()')
        if (IsSpanType(typeName) || (!method.IsStatic && arguments[0].Value is HostSpan))
            return true;
        if (typeName == "System.String")
            return method.Name == "op_Implicit" || (method.Name == "Concat" && method.Signature.ParameterTypes.All(IsSpanParameter));
        if (typeName == MemoryExtensionsType)
            return method.Name == "AsSpan" || arguments.Any(it => it.Value is HostSpan);
        if (typeName == "System.Runtime.CompilerServices.RuntimeHelpers")
            return method.Name == "CreateSpan";
        return false;
    }

    // 'newobj' on a span type: over one variable ('new ReadOnlySpan<char>(in c)', the form a concatenated char takes)
    // or over an array, whole or sliced
    public CilValue Create(ResolvedRuntimeMethod constructor, CilValue[] arguments) {
        var elementType = constructor.DeclaringType.TypeArguments[0];
        var parameters = constructor.Signature.ParameterTypes;
        if (parameters.Length == 1 && parameters[0].EndsWith('&')) {
            var item = arguments[0];
            return CilValue.FromHostValue(HostSpan.OverItems(elementType, [item.Location != null ? item : item.DereferenceLocation()]));
        }
        if (parameters.Length >= 1 && parameters[0].EndsWith("[]", StringComparison.Ordinal)) {
            var span = OverArray(elementType, arguments[0]);
            if (parameters.Length == 3)
                span = span.Slice(arguments[1].DereferenceLocation().AsInt32(), arguments[2].DereferenceLocation().AsInt32());
            return CilValue.FromHostValue(span);
        }
        throw new NotSupportedException($"'new {GetSpanName(constructor.DeclaringType.FullName)}<...>({string.Join(", ", parameters)})' is not supported in the debugger");
    }
    // 'arguments' are the call's, the receiver first for an instance member; 'constrainedType' the span type of a
    // 'constrained.' call to one of Object's virtuals; null for a call without a result
    public async Task<CilValue?> ExecuteAsync(ResolvedRuntimeMethod method, ResolvedCilType? constrainedType, CilValue[] arguments) {
        var typeName = method.DeclaringType.FullName;
        if (IsSpanType(typeName))
            return await ExecuteSpanMemberAsync(method, typeName, arguments);
        if (!method.IsStatic && arguments[0].Value is HostSpan)
            return await ExecuteSpanMemberAsync(method, constrainedType?.RuntimeType?.FullName ?? typeName, arguments);
        if (typeName == "System.String") {
            if (method.Name == "op_Implicit")
                return CilValue.FromHostValue(HostSpan.OverText(CharType, arguments[0].GetStringText() ?? string.Empty));
            return CilValue.FromPrimitive(string.Concat(arguments.Select(it => GetSpan(it).GetText())));
        }
        if (typeName == MemoryExtensionsType)
            return await ExecuteMemoryExtensionAsync(method, arguments);

        // 'CreateSpan<T>(RuntimeFieldHandle)': the data of a constant array, decoded by the element type
        var dataType = method.MethodTypeArguments[0];
        if (arguments[0].Value is not FieldDefinitionHandle dataField)
            throw new InvalidOperationException("CreateSpan requires the handle of the data field");
        return CilValue.FromHostValue(HostSpan.OverItems(dataType, DecodeFieldData(dataType, dataField)));
    }

    private async Task<CilValue?> ExecuteSpanMemberAsync(ResolvedRuntimeMethod method, string spanTypeName, CilValue[] arguments) {
        var spanName = GetSpanName(spanTypeName);
        if (method.IsStatic) {
            var elementType = method.DeclaringType.TypeArguments[0];
            if (method.Name == "op_Implicit" && arguments[0].Value is HostSpan converted)
                return CilValue.FromHostValue(converted);
            if (method.Name == "op_Implicit")
                return CilValue.FromHostValue(OverArray(elementType, arguments[0]));
            if (method.Name == "get_Empty")
                return CilValue.FromHostValue(HostSpan.OverItems(elementType, []));
            throw new NotSupportedException($"'{spanName}<...>.{method.Name}' is not supported in the debugger");
        }

        // A span of the debuggee (a frame local) has its length in a field; its elements sit behind a managed
        // pointer the debugger does not follow
        if (arguments[0].Value is not HostSpan span) {
            if (method.Name == "get_Length" && arguments[0].CorValue?.UnwrapDebugValueToObject().FindFieldValue("_length") is ICorDebugValue length)
                return CilValue.FromCorValue(length);
            throw new NotSupportedException($"'{spanName}<...>.{method.Name}' cannot be evaluated on a span of the debuggee");
        }
        switch (method.Name) {
            case "get_Length":
                return CilValue.FromPrimitive(span.Length);
            case "get_IsEmpty":
                return CilValue.FromPrimitive(span.Length == 0);
            case "get_Item":
                return CilValue.FromLocation(span.GetItem(arguments[1].AsInt32()));
            case "Slice":
                var start = arguments[1].AsInt32();
                return CilValue.FromHostValue(span.Slice(start, arguments.Length > 2 ? arguments[2].AsInt32() : span.Length - start));
            case "ToString":
                if (span.ElementType.Primitive == PrimitiveTypeCode.Char)
                    return CilValue.FromPrimitive(span.GetText());
                return CilValue.FromPrimitive($"{spanName}<{span.ElementType.Primitive?.ToString() ?? span.ElementType.RuntimeType?.FullName}>[{span.Length}]");
            case "ToArray":
                return await createArray(new HostSequence(span.ElementType, span.ToList()));
            case "GetPinnableReference":
                return CilValue.FromLocation(span.GetItem(0));
            default:
                throw new NotSupportedException($"'{spanName}<...>.{method.Name}' is not supported in the debugger");
        }
    }
    private async Task<CilValue?> ExecuteMemoryExtensionAsync(ResolvedRuntimeMethod method, CilValue[] arguments) {
        var parameters = method.Signature.ParameterTypes;
        if (method.Name == "AsSpan") {
            HostSpan span;
            if (parameters[0] == "String")
                span = HostSpan.OverText(CharType, arguments[0].GetStringText() ?? string.Empty);
            else
                span = OverArray(method.MethodTypeArguments[0], arguments[0]);
            if (parameters.Length > 1) {
                var start = arguments[1].AsInt32();
                span = span.Slice(start, parameters.Length > 2 ? arguments[2].AsInt32() : span.Length - start);
            }
            return CilValue.FromHostValue(span);
        }

        var source = GetSpan(arguments[0]);
        if (parameters.Length == 2 && arguments[1].Value is HostSpan other) {
            switch (method.Name) {
                case "SequenceEqual":
                    return CilValue.FromPrimitive(source.Length == other.Length && IndexOf(source, other) == 0);
                case "StartsWith":
                    return CilValue.FromPrimitive(other.Length <= source.Length && IndexOf(source.Slice(0, other.Length), other) == 0);
                case "EndsWith":
                    return CilValue.FromPrimitive(other.Length <= source.Length && IndexOf(source.Slice(source.Length - other.Length, other.Length), other) == 0);
                case "IndexOf":
                    return CilValue.FromPrimitive(IndexOf(source, other));
                case "Contains":
                    return CilValue.FromPrimitive(IndexOf(source, other) >= 0);
            }
        }
        if (parameters.Length == 2) {
            var value = arguments[1];
            switch (method.Name) {
                case "Contains":
                    return CilValue.FromPrimitive(IndexOf(source, value) >= 0);
                case "IndexOf":
                    return CilValue.FromPrimitive(IndexOf(source, value));
                case "LastIndexOf":
                    for (var i = source.Length - 1; i >= 0; i--) {
                        if (source.Read(i).ValueEquals(value))
                            return CilValue.FromPrimitive(i);
                    }
                    return CilValue.FromPrimitive(-1);
            }
        }
        await Task.CompletedTask;
        throw new NotSupportedException($"'MemoryExtensions.{method.Name}({string.Join(", ", parameters)})' is not supported in the debugger");
    }

    private static int IndexOf(HostSpan source, CilValue value) {
        for (var i = 0; i < source.Length; i++) {
            if (source.Read(i).ValueEquals(value))
                return i;
        }
        return -1;
    }
    private static int IndexOf(HostSpan source, HostSpan other) {
        for (var start = 0; start + other.Length <= source.Length; start++) {
            var matches = true;
            for (var i = 0; i < other.Length && matches; i++)
                matches = source.Read(start + i).ValueEquals(other.Read(i));
            if (matches)
                return start;
        }
        return -1;
    }
    private static HostSpan GetSpan(CilValue value) {
        return value.DereferenceLocation().Value as HostSpan ?? throw new NotSupportedException("A span of the debuggee cannot be read by the debugger");
    }
    // A null array converts to an empty span
    private static HostSpan OverArray(ResolvedCilType elementType, CilValue array) {
        array = array.DereferenceLocation();
        if (array.IsNull)
            return HostSpan.OverItems(elementType, []);
        return HostSpan.OverArray(elementType, array.CorValue!, array.GetArrayValue().GetCount());
    }
    private List<CilValue> DecodeFieldData(ResolvedCilType elementType, FieldDefinitionHandle dataField) {
        var corElementType = getElementType(elementType) ?? throw new NotSupportedException("A constant array of this element type is not supported in the debugger");
        var elementSize = CilValueEncoding.GetSize(corElementType);
        var data = getFieldData(dataField);
        var items = new List<CilValue>(data.Length / elementSize);
        for (var offset = 0; offset + elementSize <= data.Length; offset += elementSize)
            items.Add(CilValue.FromPrimitive(CilValueEncoding.Decode(data, offset, corElementType) ?? throw new NotSupportedException("A constant array of this element type is not supported in the debugger")));
        return items;
    }
    private static bool IsSpanParameter(string parameterType) {
        return parameterType.StartsWith(ReadOnlySpanType + "<", StringComparison.Ordinal) || parameterType.StartsWith(SpanType + "<", StringComparison.Ordinal);
    }
    private static string GetSpanName(string typeName) {
        return typeName == SpanType ? "Span" : "ReadOnlySpan";
    }
}
