using System.Collections.Immutable;

namespace DotNet.Debugging.Engine.Breakpoints;

// A parsed function breakpoint name: 'Method', 'Type.Method', 'Namespace.Type<T>.Method<U>(int, string)'
internal class FunctionBreakpointPattern {
    // Metadata form ('Namespace.Outer.Inner`1'), null when only the method name was given
    public string? TypeName { get; }
    public string MethodName { get; }
    public int? MethodArity { get; }
    // Metadata type names ('System.Int32', 'System.Nullable`1<System.Int32>'), null when no parameter list was given
    public ImmutableArray<string>? Parameters { get; }

    private FunctionBreakpointPattern(string? typeName, string methodName, int? methodArity, ImmutableArray<string>? parameters) {
        TypeName = typeName;
        MethodName = methodName;
        MethodArity = methodArity;
        Parameters = parameters;
    }

    public static FunctionBreakpointPattern Parse(string value) {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Function name cannot be empty.");
        value = value.Trim();

        ImmutableArray<string>? parameters = null;
        var openingParenthesis = FindTopLevel(value, '(');
        if (openingParenthesis >= 0) {
            if (value[^1] != ')')
                throw new ArgumentException("The function parameter list is not closed.");
            var parameterText = value.Substring(openingParenthesis + 1, value.Length - openingParenthesis - 2);
            parameters = string.IsNullOrWhiteSpace(parameterText)
                ? ImmutableArray<string>.Empty
                : SplitTopLevel(parameterText, ',').Select(NormalizeType).ToImmutableArray();
            value = value.Substring(0, openingParenthesis).Trim();
        }

        var lastDot = FindLastTopLevel(value, '.');
        var typeName = lastDot < 0 ? null : NormalizeQualifiedName(value.Substring(0, lastDot));
        var method = lastDot < 0 ? value : value.Substring(lastDot + 1);
        var (methodName, methodArity) = ParseNameSegment(method);
        if (methodName.Length == 0)
            throw new ArgumentException("Function name cannot be empty.");
        return new FunctionBreakpointPattern(typeName, methodName, methodArity, parameters);
    }

    public bool MatchesType(string candidate) {
        if (TypeName == null)
            return true;
        return candidate == TypeName || candidate.EndsWith('.' + TypeName, StringComparison.Ordinal);
    }
    public bool MatchesParameters(ImmutableArray<string> candidate) {
        if (Parameters == null)
            return true;
        if (Parameters.Value.Length != candidate.Length)
            return false;
        for (var i = 0; i < candidate.Length; i++) {
            var expected = Parameters.Value[i];
            if (candidate[i] != expected && !candidate[i].EndsWith('.' + expected, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static string NormalizeQualifiedName(string value) {
        var segments = SplitTopLevel(value, '.').Select(segment => {
            var (name, arity) = ParseNameSegment(segment);
            return arity == null ? name : $"{name}`{arity}";
        });
        return string.Join('.', segments);
    }
    private static (string Name, int? Arity) ParseNameSegment(string value) {
        value = value.Trim();
        var genericStart = FindTopLevel(value, '<');
        if (genericStart < 0)
            return (value, null);
        if (value[^1] != '>')
            throw new ArgumentException($"Generic name '{value}' is not closed.");
        var arguments = SplitTopLevel(value.Substring(genericStart + 1, value.Length - genericStart - 2), ',');
        return (value.Substring(0, genericStart).Trim(), arguments.Count);
    }
    private static string NormalizeType(string value) {
        value = string.Concat(value.Where(it => !char.IsWhiteSpace(it)));
        if (value.EndsWith('?'))
            return $"System.Nullable`1<{NormalizeType(value.Substring(0, value.Length - 1))}>";

        var genericStart = FindTopLevel(value, '<');
        if (genericStart < 0)
            return NormalizeSimpleType(value);
        if (value[^1] != '>')
            throw new ArgumentException($"Generic type '{value}' is not closed.");
        var arguments = SplitTopLevel(value.Substring(genericStart + 1, value.Length - genericStart - 2), ',').Select(NormalizeType).ToList();
        return $"{NormalizeSimpleType(value.Substring(0, genericStart))}`{arguments.Count}<{string.Join(',', arguments)}>";
    }
    private static string NormalizeSimpleType(string value) {
        return value switch {
            "bool" => "System.Boolean",
            "byte" => "System.Byte",
            "sbyte" => "System.SByte",
            "char" => "System.Char",
            "short" => "System.Int16",
            "ushort" => "System.UInt16",
            "int" => "System.Int32",
            "uint" => "System.UInt32",
            "long" => "System.Int64",
            "ulong" => "System.UInt64",
            "float" => "System.Single",
            "double" => "System.Double",
            "decimal" => "System.Decimal",
            "string" => "System.String",
            "object" => "System.Object",
            "nint" => "System.IntPtr",
            "nuint" => "System.UIntPtr",
            "void" => "System.Void",
            _ => value
        };
    }

    private static int FindTopLevel(string value, char target) {
        var depth = 0;
        for (var i = 0; i < value.Length; i++) {
            if (value[i] == target && depth == 0)
                return i;
            if (value[i] == '<')
                depth++;
            else if (value[i] == '>')
                depth--;
            if (depth < 0)
                throw new ArgumentException("Unexpected '>'.");
        }
        return -1;
    }
    private static int FindLastTopLevel(string value, char target) {
        var depth = 0;
        for (var i = value.Length - 1; i >= 0; i--) {
            if (value[i] == '>')
                depth++;
            else if (value[i] == '<')
                depth--;
            else if (value[i] == target && depth == 0)
                return i;
        }
        return -1;
    }
    private static List<string> SplitTopLevel(string value, char separator) {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++) {
            if (value[i] == '<') {
                depth++;
            }
            else if (value[i] == '>') {
                depth--;
            }
            else if (value[i] == separator && depth == 0) {
                result.Add(value.Substring(start, i - start));
                start = i + 1;
            }
        }
        result.Add(value.Substring(start));
        if (result.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A name or parameter is missing.");
        return result;
    }
}
