using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DotNet.Debugging.Remote.Generator;

// One method of a proxy: the CorApi method's parameters mapped to the arguments of an Invoke request, in the order of
// the native words, and the statements copying the results out. A parameter with no kind on the wire makes the whole
// method answer E_NOTIMPL through NotProxied
internal sealed class ProxyMethod {
    // An argument of the request: a value passed in ('in'), storage for a result ('out'), or the names' text pattern
    // ('text'). A result names the parameter it is copied to and how: a scalar through 'Convert' ('{0}' is the raw
    // value the local's 'Raw' property holds), anything else through its 'Statements'
    private sealed class Argument {
        public string Kind = string.Empty;
        public string Expression = string.Empty;
        public string Local = string.Empty;
        public string Target = string.Empty;
        public string Raw = string.Empty;
        public string Convert = string.Empty;
        public string Helper = string.Empty;
        public List<string> Statements = new List<string>();
        public int BufferIndex;
        public int CountIndex;
        public int LengthIndex;
    }

    private readonly CorApiInterface iface;
    private readonly CorApiMethod method;
    private readonly List<CorApiParameter> parameters;
    private readonly List<string> categories;
    private readonly bool explicitImplementation;
    private readonly HashSet<string> proxied;
    private readonly Dictionary<string, CorApiInterface> interfaces;
    private readonly InterfaceIds ids;

    public ProxyMethod(CorApiInterface iface, CorApiMethod method, bool explicitImplementation, HashSet<string> proxied, Dictionary<string, CorApiInterface> interfaces, TypeKinds kinds, InterfaceIds ids) {
        this.iface = iface;
        this.method = method;
        this.explicitImplementation = explicitImplementation;
        this.proxied = proxied;
        this.interfaces = interfaces;
        this.ids = ids;
        parameters = method.Parameters;
        categories = parameters.Select(kinds.Categorize).ToList();
    }

    public void Write(StringBuilder text) {
        var returns = method.ReturnsVoid ? "void" : "int";
        var name = explicitImplementation ? $"{returns} {iface.Name}.{method.Name}" : $"public {returns} {method.Name}";
        text.Append($"    {name}({string.Join(", ", parameters.Select(p => p.Declaration()))}) {{\n");
        var arguments = new List<Argument>();
        if (!TryMap(arguments, out var unmapped)) {
            text.Append($"        // '{unmapped}' has no kind on the wire\n");
            foreach (var assignment in parameters.Select(DefaultAssignment).Where(a => a != null))
                text.Append($"        {assignment}\n");
            text.Append($"        return NotProxied(\"{method.Name.Substring(3)}\");\n");
            text.Append("    }\n");
            return;
        }
        WriteBody(text, arguments);
        text.Append("    }\n");
    }

    private string Category(CorApiParameter parameter) {
        return categories[parameters.IndexOf(parameter)];
    }
    // The value the proxy assigns to an out parameter it cannot serve
    private string? DefaultAssignment(CorApiParameter p) {
        if (!p.IsOut)
            return null;
        var category = Category(p);
        if (category == "object")
            return $"{p.Identifier} = null!;";
        if (category == "u32" || category == "int" || category == "u64" || category == "nuint" || category == "nint")
            return $"{p.Identifier} = 0;";
        return $"{p.Identifier} = default;";
    }
    private void WriteBody(StringBuilder text, List<Argument> arguments) {
        var slot = iface.Slot(method, interfaces);
        var id = ids.Reference(iface);
        var ins = arguments.Where(a => a.Kind == "in").Select(a => a.Expression).ToList();
        var outs = arguments.Where(a => a.Kind != "in").ToList();
        var insText = string.Concat(ins.Select(e => ", " + e));
        if (method.ReturnsVoid) {
            text.Append($"        Invoke({id}, {slot}{insText});\n");
            return;
        }
        if (outs.Count == 0) {
            text.Append($"        return Invoke({id}, {slot}{insText});\n");
            return;
        }
        // One result, last, of a kind with a helper: the helpers keep it short
        var last = arguments.Last();
        if (outs.Count == 1 && last.Kind == "out" && last.Helper != string.Empty) {
            if (last.Helper == "Object") {
                text.Append($"        return InvokeObject({id}, {slot}, out {last.Target}{insText});\n");
                return;
            }
            if (last.Convert == "{0}") {
                text.Append($"        return Invoke{last.Helper}({id}, {slot}, out {last.Target}{insText});\n");
                return;
            }
            if (last.Helper == "UInt32" && last.Convert == "{0} != 0") {
                text.Append($"        return InvokeBool({id}, {slot}, out {last.Target}{insText});\n");
                return;
            }
            text.Append($"        var hr = Invoke{last.Helper}({id}, {slot}, out var {last.Target}Value{insText});\n");
            text.Append($"        {last.Target} = {string.Format(last.Convert, last.Target + "Value")};\n");
            text.Append("        return hr;\n");
            return;
        }
        if (outs.Count == 1 && last.Kind == "text" && arguments.Count == 1) {
            text.Append($"        return InvokeText({id}, {slot}, {parameters[last.CountIndex].Identifier}, out {parameters[last.LengthIndex].Identifier}, {parameters[last.BufferIndex].Identifier});\n");
            return;
        }
        foreach (var argument in outs)
            text.Append($"        var {argument.Local} = {argument.Expression};\n");
        var call = string.Join(", ", arguments.Select(a => a.Kind == "in" ? a.Expression : a.Local));
        text.Append($"        var hr = Invoke({id}, {slot}, {call});\n");
        foreach (var argument in outs) {
            if (argument.Convert != string.Empty)
                text.Append($"        {argument.Target} = {string.Format(argument.Convert, argument.Local + "." + argument.Raw)};\n");
            foreach (var statement in argument.Statements)
                text.Append($"        {statement}\n");
        }
        text.Append("        return hr;\n");
    }

    private bool TryMap(List<Argument> arguments, out string unmapped) {
        unmapped = string.Empty;
        var groups = new Dictionary<int, int[]>();
        var consumed = new HashSet<int>();
        // The names' patterns, (cch, out pcch, char[]) and (char[], cch, out pch); the blob pairs (out pointer, out
        // count) the callee points into its own memory; a pointer to bytes in this process with its count
        for (var i = 0; i < parameters.Count; i++) {
            var p = parameters[i];
            var category = Category(p);
            if (p.IsArray && category == "chars") {
                var countIndex = parameters.FindIndex(q => q.Name == p.CountName);
                var lengthIndex = countIndex + 1;
                if (countIndex < 0 || lengthIndex >= parameters.Count || !parameters[lengthIndex].IsOut || Category(parameters[lengthIndex]) != "u32") {
                    unmapped = p.Identifier + " (count " + (p.CountName ?? "?") + ")";
                    return false;
                }
                groups[System.Math.Min(i, System.Math.Min(countIndex, lengthIndex))] = new[] { 0, i, countIndex, lengthIndex };
                consumed.Add(i);
                consumed.Add(countIndex);
                consumed.Add(lengthIndex);
            }
            else if (p.IsOut && (category == "nint" || category == "bytepointer") && i + 1 < parameters.Count && parameters[i + 1].IsOut && Category(parameters[i + 1]) == "u32") {
                groups[i] = new[] { 1, i, i + 1 };
                consumed.Add(i);
                consumed.Add(i + 1);
            }
            else if (category == "bytepointer" && !p.IsOut && i + 1 < parameters.Count && Category(parameters[i + 1]) == "u32" && parameters[i + 1].Name.StartsWith("cb")) {
                groups[i] = new[] { 2, i, i + 1 };
                consumed.Add(i);
            }
        }
        for (var i = 0; i < parameters.Count; i++) {
            if (groups.TryGetValue(i, out var group)) {
                if (group[0] == 0)
                    arguments.Add(TextArgument(group));
                else if (group[0] == 1)
                    arguments.Add(BlobArgument(parameters[group[1]], parameters[group[2]]));
                else
                    arguments.Add(new Argument { Kind = "in", Expression = $"RemoteArgument.Bytes(BytesAt((nint){parameters[group[1]].Identifier}, {parameters[group[2]].Identifier}))" });
                continue;
            }
            if (consumed.Contains(i))
                continue;
            var parameter = parameters[i];
            if (parameter.IsRef && Category(parameter) == "hcorenum") {
                var argument = new Argument { Kind = "out", Local = parameter.Identifier + "Result", Target = parameter.Identifier, Raw = "UInt64Result", Convert = "Unsafe.BitCast<nint, HCorEnum>((nint){0})" };
                argument.Expression = $"RemoteArgument.RefUInt64((ulong)Unsafe.BitCast<HCorEnum, nint>({parameter.Identifier}))";
                arguments.Add(argument);
                continue;
            }
            if (parameter.IsOut || (parameter.IsArray && parameter.AttrOut)) {
                var argument = MapOut(parameter);
                if (argument == null) {
                    unmapped = parameter.Identifier;
                    return false;
                }
                arguments.Add(argument);
                continue;
            }
            var expression = InExpression(parameter);
            if (expression == null) {
                unmapped = parameter.Identifier;
                return false;
            }
            arguments.Add(new Argument { Kind = "in", Expression = expression });
        }
        return true;
    }
    private Argument TextArgument(int[] group) {
        var buffer = parameters[group[1]];
        var count = parameters[group[2]];
        var length = parameters[group[3]];
        var factory = group[2] < group[1] ? $"RemoteArgument.OutText({count.Identifier})" : $"RemoteArgument.OutTextBuffer({count.Identifier})";
        var argument = new Argument { Kind = "text", Local = buffer.Identifier + "Result", Expression = factory, BufferIndex = group[1], CountIndex = group[2], LengthIndex = group[3] };
        argument.Statements.Add($"{length.Identifier} = {buffer.Identifier}Result.LengthResult;");
        argument.Statements.Add($"CopyText({buffer.Identifier}Result.TextResult, {buffer.Identifier});");
        return argument;
    }
    private Argument BlobArgument(CorApiParameter pointer, CorApiParameter count) {
        var chars = count.Name.StartsWith("pcch");
        var cast = Category(pointer) == "bytepointer" ? "(byte*)" : string.Empty;
        var argument = new Argument { Kind = "out", Local = pointer.Identifier + "Result", Expression = $"RemoteArgument.OutBlob({(chars ? 2 : 1)}, {(chars ? 8 : 0)})" };
        argument.Statements.Add($"{pointer.Identifier} = {cast}KeepBlob({pointer.Identifier}Result.BytesResult);");
        argument.Statements.Add($"{count.Identifier} = {pointer.Identifier}Result.UInt32Result;");
        return argument;
    }
    private Argument? MapOut(CorApiParameter p) {
        var argument = new Argument { Kind = "out", Local = p.Identifier + "Result", Target = p.Identifier };
        var category = Category(p);
        var typeText = p.TypeText.TrimEnd('?');
        if (p.IsArray) {
            switch (category) {
                case "object":
                    if (!proxied.Contains(p.ElementTypeName))
                        return null;
                    argument.Expression = $"RemoteArgument.OutObjects({p.CountName}, RemoteSession.ProbeFor(typeof({p.ElementTypeName})))";
                    argument.Statements.Add($"FillProxies({p.Identifier}, {argument.Local}.HandlesResult, {argument.Local}.FlagsResults);");
                    return argument;
                case "bytes":
                    argument.Expression = $"RemoteArgument.OutBytes({p.CountName})";
                    argument.Statements.Add($"CopyBytes({argument.Local}.BytesResult, {p.Identifier});");
                    return argument;
                case "u32":
                case "u64":
                case "token":
                case "enum":
                case "struct":
                    if (p.HasCustomMarshaller)
                        return null;
                    argument.Expression = $"RemoteArgument.OutBytes({p.CountName} * (uint)Unsafe.SizeOf<{p.ElementTypeName}>())";
                    argument.Statements.Add($"CopyValues({argument.Local}.BytesResult, {p.Identifier});");
                    return argument;
                default:
                    return null;
            }
        }
        switch (category) {
            case "u32": return Scalar(argument, "UInt32", "{0}");
            case "int": return Scalar(argument, "UInt32", "(int){0}");
            case "bool": return Scalar(argument, "UInt32", "{0} != 0");
            case "enum": return Scalar(argument, "UInt32", $"({typeText}){{0}}");
            case "token": return Scalar(argument, "UInt32", $"new {typeText}({{0}})");
            case "u64": return Scalar(argument, "UInt64", "{0}");
            case "nuint": return Scalar(argument, "UInt64", "(nuint){0}");
            case "address": return Scalar(argument, "UInt64", "new CordbAddress({0})");
            case "guid":
                argument.Expression = "RemoteArgument.OutGuid()";
                argument.Raw = "GuidResult";
                argument.Convert = "{0}";
                return argument;
            case "struct":
                if (p.HasCustomMarshaller)
                    return null;
                argument.Expression = $"RemoteArgument.OutBytes((uint)Unsafe.SizeOf<{typeText}>())";
                argument.Statements.Add($"{p.Identifier} = ReadValue<{typeText}>({argument.Local}.BytesResult);");
                return argument;
            case "object":
                if (!proxied.Contains(p.ElementTypeName))
                    return null;
                argument.Expression = $"RemoteArgument.OutObject(RemoteSession.ProbeFor(typeof({typeText})))";
                argument.Helper = "Object";
                argument.Statements.Add($"{p.Identifier} = Session.GetProxy<{typeText}>({argument.Local}.HandleResult, {argument.Local}.FlagsResult)!;");
                return argument;
            default:
                return null;
        }
    }
    // An integer result: 'helper' names the wire size (UInt32 or UInt64) and the Invoke helper serving it alone
    private static Argument Scalar(Argument argument, string helper, string convert) {
        argument.Expression = $"RemoteArgument.Out{helper}()";
        argument.Raw = helper + "Result";
        argument.Convert = convert;
        argument.Helper = helper;
        return argument;
    }
    private string? InExpression(CorApiParameter p) {
        var category = Category(p);
        if (p.IsArray) {
            switch (category) {
                case "object": return $"RemoteArgument.Objects(HandlesOf({p.Identifier}), {ids.Reference(interfaces[p.ElementTypeName])})";
                case "bytes": return $"RemoteArgument.Bytes({p.Identifier})";
                case "u32":
                case "u64":
                case "token":
                case "enum":
                case "struct":
                    return p.HasCustomMarshaller ? null : $"RemoteArgument.Bytes(BytesOf({p.Identifier}))";
                default: return null;
            }
        }
        switch (category) {
            case "u32": return $"RemoteArgument.UInt32({p.Identifier})";
            case "int": return $"RemoteArgument.UInt32((uint){p.Identifier})";
            case "bool": return $"RemoteArgument.Bool({p.Identifier})";
            case "enum": return $"RemoteArgument.UInt32((uint){p.Identifier})";
            case "token": return $"RemoteArgument.UInt32({p.Identifier}.Value)";
            case "u64": return $"RemoteArgument.UInt64({p.Identifier})";
            case "address": return $"RemoteArgument.UInt64({p.Identifier}.Value)";
            case "hcorenum": return $"RemoteArgument.UInt64((ulong)Unsafe.BitCast<HCorEnum, nint>({p.Identifier}))";
            case "text": return $"RemoteArgument.Text({p.Identifier})";
            case "guid": return p.IsRef ? $"RemoteArgument.Iid({p.Identifier})" : null;
            case "object": return interfaces.ContainsKey(p.ElementTypeName) ? $"RemoteArgument.Object(HandleOf({p.Identifier}), {ids.Reference(interfaces[p.ElementTypeName])})" : null;
            default: return null;
        }
    }
}
