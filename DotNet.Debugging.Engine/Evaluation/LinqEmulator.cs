using System.Reflection.Emit;
using System.Reflection.Metadata;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;

namespace DotNet.Debugging.Engine.Evaluation;

// System.Linq operators run on the host when a lambda is involved, the lambda only existing in the debugger: the
// source is enumerated into interpreter values, the lambda is interpreted per element, and the result is a value or
// a host sequence (materialized into a debuggee array once it leaves the interpreter). An operator without a lambda
// keeps running in the debuggee unless its source is such a sequence
internal class LinqEmulator {
    private const string InvalidOperation = "System.InvalidOperationException";

    private readonly Func<CilValue, CilValue[], Task<CilValue>> invoke;
    private readonly Func<CilValue, ResolvedCilType, Task<List<CilValue>>> enumerate;
    private readonly Func<ResolvedCilType, Task<CilValue>> createDefault;
    private readonly Func<HostSequence, Task<CilValue>> createArray;
    private readonly Func<HostSequence, Task<CilValue>> createList;

    public LinqEmulator(
        Func<CilValue, CilValue[], Task<CilValue>> invoke,
        Func<CilValue, ResolvedCilType, Task<List<CilValue>>> enumerate,
        Func<ResolvedCilType, Task<CilValue>> createDefault,
        Func<HostSequence, Task<CilValue>> createArray,
        Func<HostSequence, Task<CilValue>> createList) {
        this.invoke = invoke;
        this.enumerate = enumerate;
        this.createDefault = createDefault;
        this.createArray = createArray;
        this.createList = createList;
    }

    // 'arguments' are the operator's, the source first
    public async Task<CilValue> ExecuteAsync(ResolvedRuntimeMethod method, CilValue[] arguments) {
        var source = arguments[0];
        var elementType = GetElementType(method, source);
        var items = await enumerate(source, elementType);
        var arity = arguments.Length > 1 ? GetDelegateArity(method, 1) : 0;
        switch (method.Name) {
            case "Where":
                return Sequence(elementType, await FilterAsync(items, arguments[1], arity));
            case "Select":
                return Sequence(method.MethodTypeArguments[1], await MapAsync(items, arguments[1], arity));
            case "SelectMany" when arguments.Length == 2: {
                var resultType = method.MethodTypeArguments[1];
                var results = new List<CilValue>();
                foreach (var inner in await MapAsync(items, arguments[1], arity))
                    results.AddRange(await enumerate(inner, resultType));
                return Sequence(resultType, results);
            }
            case "Any":
                return Boolean(arguments.Length == 1 ? items.Count > 0 : await FindIndexAsync(items, arguments[1], arity, expected: true) >= 0);
            case "All":
                return Boolean(await FindIndexAsync(items, arguments[1], arity, expected: false) < 0);
            case "Count":
            case "LongCount": {
                var count = arguments.Length == 1 ? items.Count : (await FilterAsync(items, arguments[1], arity)).Count;
                return method.Name == "Count" ? CilValue.FromPrimitive(count) : CilValue.FromPrimitive((long)count);
            }
            case "First":
            case "FirstOrDefault":
            case "Last":
            case "LastOrDefault":
            case "Single":
            case "SingleOrDefault":
                return await PickAsync(method, items, arguments, elementType, arity);
            case "ElementAt":
            case "ElementAtOrDefault": {
                var index = arguments[1].AsInt32();
                if (index >= 0 && index < items.Count)
                    return items[index];
                if (method.Name == "ElementAt")
                    throw new EvaluationThrewException("System.ArgumentOutOfRangeException");
                return await createDefault(elementType);
            }
            case "Sum":
                return await SumAsync(items, arguments.Length > 1 ? arguments[1] : null, arity, method.Signature.ReturnType);
            case "Average":
                return await AverageAsync(items, arguments.Length > 1 ? arguments[1] : null, arity, method, elementType);
            case "Min":
            case "Max":
                return await ExtremumAsync(items, arguments.Length > 1 ? arguments[1] : null, arity, method, elementType);
            case "Aggregate":
                return await AggregateAsync(items, arguments);
            case "OrderBy":
            case "OrderByDescending": {
                var sequence = new HostSequence(elementType, items);
                sequence.Orderings.Add(new HostOrdering(await MapAsync(items, arguments[1], arity), method.Name == "OrderByDescending"));
                Sort(sequence);
                return CilValue.FromHostValue(sequence);
            }
            case "ThenBy":
            case "ThenByDescending": {
                if (Dereference(source).Value is not HostSequence ordered)
                    throw new NotSupportedException("'ThenBy' continues an ordering the debuggee computed, which the debugger cannot");
                var sequence = new HostSequence(elementType, items);
                sequence.Orderings.AddRange(ordered.Orderings);
                sequence.Orderings.Add(new HostOrdering(await MapAsync(items, arguments[1], arity), method.Name == "ThenByDescending"));
                Sort(sequence);
                return CilValue.FromHostValue(sequence);
            }
            case "Skip":
                return Sequence(elementType, items.Skip(arguments[1].AsInt32()).ToList());
            case "Take":
                return Sequence(elementType, items.Take(arguments[1].AsInt32()).ToList());
            case "SkipWhile":
            case "TakeWhile": {
                var boundary = await FindIndexAsync(items, arguments[1], arity, expected: false);
                if (boundary < 0)
                    boundary = items.Count;
                return Sequence(elementType, (method.Name == "TakeWhile" ? items.Take(boundary) : items.Skip(boundary)).ToList());
            }
            case "Distinct" when arguments.Length == 1: {
                var distinct = new List<CilValue>();
                foreach (var item in items) {
                    if (!distinct.Any(it => ValuesEqual(it, item)))
                        distinct.Add(item);
                }
                return Sequence(elementType, distinct);
            }
            case "Reverse":
                items.Reverse();
                return Sequence(elementType, items);
            case "Contains" when arguments.Length == 2:
                return Boolean(items.Any(it => ValuesEqual(it, arguments[1])));
            case "Concat":
                items.AddRange(await enumerate(arguments[1], elementType));
                return Sequence(elementType, items);
            case "ToList":
                return await createList(new HostSequence(elementType, items));
            case "ToArray":
                return await createArray(new HostSequence(elementType, items));
        }
        throw new NotSupportedException($"'System.Linq.Enumerable.{method.Name}' is not supported with a lambda or a sequence the debugger computed");
    }

    private async Task<List<CilValue>> FilterAsync(List<CilValue> items, CilValue predicate, int arity) {
        var result = new List<CilValue>();
        for (var i = 0; i < items.Count; i++) {
            if ((await InvokeAsync(predicate, items[i], i, arity)).IsTrue())
                result.Add(items[i]);
        }
        return result;
    }
    private async Task<List<CilValue>> MapAsync(List<CilValue> items, CilValue selector, int arity) {
        var result = new List<CilValue>(items.Count);
        for (var i = 0; i < items.Count; i++)
            result.Add(await InvokeAsync(selector, items[i], i, arity));
        return result;
    }
    // The first index whose predicate result is 'expected', -1 when there is none
    private async Task<int> FindIndexAsync(List<CilValue> items, CilValue predicate, int arity, bool expected) {
        for (var i = 0; i < items.Count; i++) {
            if ((await InvokeAsync(predicate, items[i], i, arity)).IsTrue() == expected)
                return i;
        }
        return -1;
    }
    // A two-parameter lambda of Where/Select/SkipWhile/TakeWhile also gets the element's index
    private Task<CilValue> InvokeAsync(CilValue function, CilValue item, int index, int arity) {
        return arity >= 2 ? invoke(function, [item, CilValue.FromPrimitive(index)]) : invoke(function, [item]);
    }

    private async Task<CilValue> PickAsync(ResolvedRuntimeMethod method, List<CilValue> items, CilValue[] arguments, ResolvedCilType elementType, int arity) {
        var hasPredicate = arguments.Length > 1 && IsDelegateParameter(method, 1);
        var matches = hasPredicate ? await FilterAsync(items, arguments[1], arity) : items;
        // 'FirstOrDefault(defaultValue)' and 'FirstOrDefault(predicate, defaultValue)' name the fallback
        var fallback = arguments.Length > (hasPredicate ? 2 : 1) ? arguments[^1] : null;
        var name = method.Name;
        if (name is "Single" or "SingleOrDefault" && matches.Count > 1)
            throw new EvaluationThrewException(InvalidOperation);
        if (matches.Count > 0)
            return name.StartsWith("Last", StringComparison.Ordinal) ? matches[^1] : matches[0];
        if (!name.EndsWith("OrDefault", StringComparison.Ordinal))
            throw new EvaluationThrewException(InvalidOperation);
        return fallback ?? await createDefault(elementType);
    }
    // 'returnType' is the operator's: Sum adds checked in that type, which is also the accumulator of an Average
    private async Task<CilValue> SumAsync(List<CilValue> items, CilValue? selector, int arity, string returnType) {
        CilValue sum;
        switch (returnType) {
            case "Int32": sum = CilValue.FromPrimitive(0); break;
            case "Int64": sum = CilValue.FromPrimitive(0L); break;
            case "Single": sum = CilValue.FromPrimitive(0f); break;
            case "Double": sum = CilValue.FromPrimitive(0d); break;
            default: throw new NotSupportedException($"A sum of '{returnType}' values is not supported in the debugger");
        }
        var addition = returnType is "Single" or "Double" ? OpCodes.Add : OpCodes.Add_Ovf;
        for (var i = 0; i < items.Count; i++) {
            var value = selector == null ? items[i] : await InvokeAsync(selector, items[i], i, arity);
            if (value.IsNull)
                continue;
            sum = CilInterpreter.EvaluateBinary(addition, sum, value);
        }
        return sum;
    }
    private async Task<CilValue> AverageAsync(List<CilValue> items, CilValue? selector, int arity, ResolvedRuntimeMethod method, ResolvedCilType elementType) {
        var valueType = selector == null ? elementType.Primitive?.ToString() : GetLastGenericArgument(method.Signature.ParameterTypes[1]);
        string accumulatorType;
        if (valueType is "Int32" or "Int64")
            accumulatorType = "Int64";
        else if (valueType is "Single" or "Double")
            accumulatorType = "Double";
        else
            throw new NotSupportedException($"An average of '{valueType}' values is not supported in the debugger");

        if (items.Count == 0)
            throw new EvaluationThrewException(InvalidOperation);
        var sum = await SumAsync(items, selector, arity, accumulatorType);
        var average = accumulatorType == "Int64" ? sum.AsInt64() / (double)items.Count : sum.AsFloat() / items.Count;
        if (method.Signature.ReturnType == "Single")
            return CilValue.FromPrimitive((float)average);
        return CilValue.FromPrimitive(average);
    }
    private async Task<CilValue> ExtremumAsync(List<CilValue> items, CilValue? selector, int arity, ResolvedRuntimeMethod method, ResolvedCilType elementType) {
        var isMax = method.Name == "Max";
        CilValue? best = null;
        for (var i = 0; i < items.Count; i++) {
            var value = selector == null ? items[i] : await InvokeAsync(selector, items[i], i, arity);
            if (value.IsNull)
                continue;
            if (best == null || (isMax ? CompareValues(value, best) > 0 : CompareValues(value, best) < 0))
                best = value;
        }
        if (best != null)
            return best;

        // An empty sequence of values has no extremum, one of references yields null
        ResolvedCilType resultType;
        if (selector == null)
            resultType = elementType;
        else if (method.MethodTypeArguments.Length > 1)
            resultType = method.MethodTypeArguments[1];
        else
            resultType = ResolvedCilType.FromPrimitive(Enum.Parse<PrimitiveTypeCode>(GetLastGenericArgument(method.Signature.ParameterTypes[1])));
        if (IsValueType(resultType))
            throw new EvaluationThrewException(InvalidOperation);
        return CilValue.Null();
    }
    private async Task<CilValue> AggregateAsync(List<CilValue> items, CilValue[] arguments) {
        if (arguments.Length == 2) {
            if (items.Count == 0)
                throw new EvaluationThrewException(InvalidOperation);
            var accumulated = items[0];
            for (var i = 1; i < items.Count; i++)
                accumulated = await invoke(arguments[1], [accumulated, items[i]]);
            return accumulated;
        }
        var accumulator = arguments[1];
        foreach (var item in items)
            accumulator = await invoke(arguments[2], [accumulator, item]);
        return arguments.Length == 4 ? await invoke(arguments[3], [accumulator]) : accumulator;
    }

    // A stable sort by every ordering in turn: the index breaks the ties, so equal keys keep their order
    private static void Sort(HostSequence sequence) {
        var order = Enumerable.Range(0, sequence.Items.Count).ToList();
        order.Sort((left, right) => {
            foreach (var ordering in sequence.Orderings) {
                var result = CompareValues(ordering.Keys[left], ordering.Keys[right]);
                if (ordering.Descending)
                    result = -result;
                if (result != 0)
                    return result;
            }
            return left.CompareTo(right);
        });
        Reorder(sequence.Items, order);
        foreach (var ordering in sequence.Orderings)
            Reorder(ordering.Keys, order);
    }
    private static void Reorder(List<CilValue> values, List<int> order) {
        var reordered = order.Select(it => values[it]).ToList();
        values.Clear();
        values.AddRange(reordered);
    }
    // The order Comparer<T>.Default gives: null first, strings by the current culture, numbers by value
    private static int CompareValues(CilValue left, CilValue right) {
        if (left.IsNull || right.IsNull)
            return left.IsNull ? (right.IsNull ? 0 : -1) : 1;
        var leftText = left.GetStringText();
        var rightText = right.GetStringText();
        if (leftText != null && rightText != null)
            return string.Compare(leftText, rightText, StringComparison.CurrentCulture);
        if (CilInterpreter.Compare(OpCodes.Clt, left, right))
            return -1;
        return CilInterpreter.Compare(OpCodes.Cgt, left, right) ? 1 : 0;
    }
    // The equality EqualityComparer<T>.Default gives: strings by text, values by content, references by identity
    private static bool ValuesEqual(CilValue left, CilValue right) {
        if (left.IsNull || right.IsNull)
            return left.IsNull && right.IsNull;
        var leftText = left.GetStringText();
        var rightText = right.GetStringText();
        if (leftText != null || rightText != null)
            return leftText == rightText;
        if (left.Value == null && right.Value == null
                && left.CorValue!.UnwrapDebugValue() is ICorDebugGenericValue leftGeneric && left.CorValue is not ICorDebugReferenceValue
                && right.CorValue!.UnwrapDebugValue() is ICorDebugGenericValue rightGeneric && right.CorValue is not ICorDebugReferenceValue)
            return leftGeneric.GetValueAsBytes().AsSpan().SequenceEqual(rightGeneric.GetValueAsBytes());
        return CilInterpreter.Compare(OpCodes.Ceq, left, right);
    }

    private static ResolvedCilType GetElementType(ResolvedRuntimeMethod method, CilValue source) {
        if (Dereference(source).Value is HostSequence sequence)
            return sequence.ElementType;
        if (!method.MethodTypeArguments.IsDefaultOrEmpty)
            return method.MethodTypeArguments[0];
        // A non generic overload ('Sum(IEnumerable<int>)'): the element type is the parameter's argument
        var parameter = method.Signature.ParameterTypes[0];
        if (Enum.TryParse<PrimitiveTypeCode>(GetLastGenericArgument(parameter), out var primitive))
            return ResolvedCilType.FromPrimitive(primitive);
        throw new NotSupportedException($"The element type of '{parameter}' is not supported in the debugger");
    }
    private static bool IsDelegateParameter(ResolvedRuntimeMethod method, int index) {
        return method.Signature.ParameterTypes[index].StartsWith("System.Func`", StringComparison.Ordinal);
    }
    // The number of arguments the 'Func' parameter takes ('System.Func`3<!!0,Int32,Boolean>' takes two), 0 for another parameter
    private static int GetDelegateArity(ResolvedRuntimeMethod method, int index) {
        var parameter = method.Signature.ParameterTypes[index];
        if (!IsDelegateParameter(method, index))
            return 0;
        var start = "System.Func`".Length;
        var end = parameter.IndexOf('<', start);
        return int.Parse(parameter.AsSpan(start, end - start)) - 1;
    }
    // 'Int32' out of 'System.Func`2<!!0,Int32>' or 'System.Collections.Generic.IEnumerable`1<Int32>'
    private static string GetLastGenericArgument(string type) {
        var end = type.LastIndexOf('>');
        if (end < 0)
            return type;
        var start = Math.Max(type.LastIndexOf(',', end), type.IndexOf('<'));
        return type.Substring(start + 1, end - start - 1);
    }
    private static bool IsValueType(ResolvedCilType type) {
        if (type.Primitive != null)
            return type.Primitive != PrimitiveTypeCode.String && type.Primitive != PrimitiveTypeCode.Object;
        return type.RuntimeType != null && EvaluationMetadataResolver.IsValueType(type.RuntimeType);
    }
    private static CilValue Sequence(ResolvedCilType elementType, List<CilValue> items) {
        return CilValue.FromHostValue(new HostSequence(elementType, items));
    }
    private static CilValue Boolean(bool value) {
        return CilValue.FromPrimitive(value);
    }
    private static CilValue Dereference(CilValue value) {
        return value.Location != null ? value.Dereference() : value;
    }
}
