using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNet.Debugging.Remote.Generator;

// A CorApi interface as declared in its source file: its methods in vtable order, its base interface, its id. The
// definitions are read from the source rather than the compiled assembly because [In], [Out], [PreserveSig] and
// SizeParamIndex are pseudo-attributes the compiler folds into metadata flags, where symbols no longer show them
internal sealed class CorApiInterface {
    public const string Namespace = "DotNet.Debugging.CorApi";

    public string Name { get; }
    public string? BaseName { get; }
    public string? Guid { get; }
    public List<CorApiMethod> Methods { get; }

    private CorApiInterface(InterfaceDeclarationSyntax declaration) {
        Name = declaration.Identifier.Text;
        BaseName = declaration.BaseList?.Types.Select(t => t.Type.ToString()).FirstOrDefault(n => n.StartsWith("ICorDebug") || n.StartsWith("IMetaData"));
        Guid = declaration.AttributeLists.SelectMany(l => l.Attributes)
            .Where(a => a.Name.ToString() == "Guid" && a.ArgumentList != null && a.ArgumentList.Arguments.Count == 1)
            .Select(a => a.ArgumentList!.Arguments[0].Expression)
            .OfType<LiteralExpressionSyntax>()
            .Select(e => e.Token.ValueText)
            .FirstOrDefault();
        Methods = declaration.Members.OfType<MethodDeclarationSyntax>()
            .Where(m => m.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() == "PreserveSig"))
            .Select(m => new CorApiMethod(m))
            .ToList();
    }

    // Every interface declared in the given source files, by name
    public static Dictionary<string, CorApiInterface> Parse(IEnumerable<string> sources) {
        var result = new Dictionary<string, CorApiInterface>();
        foreach (var source in sources) {
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            foreach (var declaration in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>()) {
                var iface = new CorApiInterface(declaration);
                result[iface.Name] = iface;
            }
        }
        return result;
    }
    // ICorDebugThread -> Thread, IMetaDataImport -> MetaDataImport
    public static string IdName(string name) {
        if (name.StartsWith("ICorDebug"))
            return name.Substring("ICorDebug".Length);
        if (name.StartsWith("IMetaData"))
            return "MetaData" + name.Substring("IMetaData".Length);
        return name;
    }

    // The interfaces this one derives from, the nearest first (a CorApi interface has at most one)
    public List<CorApiInterface> BaseChain(Dictionary<string, CorApiInterface> interfaces) {
        var chain = new List<CorApiInterface>();
        var current = BaseName;
        while (current != null && interfaces.TryGetValue(current, out var baseInterface)) {
            chain.Add(baseInterface);
            current = baseInterface.BaseName;
        }
        return chain;
    }
    // The vtable slot: IUnknown's three, the base interfaces' methods, then the method's index
    public int Slot(CorApiMethod method, Dictionary<string, CorApiInterface> interfaces) {
        var slot = 3 + Methods.IndexOf(method);
        foreach (var baseInterface in BaseChain(interfaces))
            slot += baseInterface.Methods.Count;
        return slot;
    }
}

internal sealed class CorApiMethod {
    public string Name { get; }
    public bool ReturnsVoid { get; }
    public List<CorApiParameter> Parameters { get; }

    public CorApiMethod(MethodDeclarationSyntax declaration) {
        Name = declaration.Identifier.Text;
        ReturnsVoid = declaration.ReturnType.ToString() == "void";
        Parameters = declaration.ParameterList.Parameters.Select(p => new CorApiParameter(p)).ToList();
        foreach (var parameter in Parameters) {
            if (parameter.CountIndex >= 0 && parameter.CountIndex < Parameters.Count)
                parameter.CountName = Parameters[parameter.CountIndex].Name;
        }
    }

    // Name and parameter types: two interfaces of one class declaring the same are implemented explicitly
    public string Key() {
        return Name + "(" + string.Join(",", Parameters.Select(p => p.Declaration())) + ")";
    }
}

// A parameter as declared: its type text, its direction, the attributes that say how it travels
internal sealed class CorApiParameter {
    public string Name { get; }
    // The type as written, nullable annotation included (char[]?, ICorDebugType[]?)
    public string TypeText { get; }
    public string ElementTypeName { get; }
    public bool IsArray { get; }
    public bool IsPointer { get; }
    public bool IsOut { get; }
    public bool IsRef { get; }
    public bool AttrIn { get; }
    public bool AttrOut { get; }
    // The parameter holding the element count of an array or buffer, by name or by index
    public string? CountName { get; set; }
    public int CountIndex { get; }
    // A marshaller of its own means a layout the wire has no kind for
    public bool HasCustomMarshaller { get; }
    // The name as written in C#, keywords escaped
    public string Identifier { get; }

    public CorApiParameter(ParameterSyntax declaration) {
        Name = declaration.Identifier.Text;
        Identifier = SyntaxFacts.GetKeywordKind(Name) != SyntaxKind.None ? "@" + Name : Name;
        TypeText = declaration.Type?.ToString() ?? "object";
        var bare = TypeText.TrimEnd('?');
        IsArray = bare.EndsWith("[]");
        IsPointer = bare.EndsWith("*");
        ElementTypeName = IsArray ? bare.Substring(0, bare.Length - 2) : IsPointer ? bare.Substring(0, bare.Length - 1) : bare;
        IsOut = declaration.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword));
        IsRef = declaration.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword));
        CountIndex = -1;
        foreach (var attribute in declaration.AttributeLists.SelectMany(l => l.Attributes)) {
            var name = attribute.Name.ToString();
            if (name == "In")
                AttrIn = true;
            if (name == "Out")
                AttrOut = true;
            if (attribute.ArgumentList == null)
                continue;
            foreach (var argument in attribute.ArgumentList.Arguments) {
                var named = argument.NameEquals?.Name.Identifier.Text;
                var value = argument.Expression;
                if (name == "MarshalUsing" && named == null && value is TypeOfExpressionSyntax)
                    HasCustomMarshaller = true;
                if (name == "MarshalUsing" && named == "CountElementName" && value is LiteralExpressionSyntax literal)
                    CountName = literal.Token.ValueText;
                if (name == "MarshalAs" && named == "SizeParamIndex" && value is LiteralExpressionSyntax index && int.TryParse(index.Token.ValueText, out var parsed))
                    CountIndex = parsed;
            }
        }
    }

    // The declaration in the proxy's signature: [In]/[Out] and the marshalling attributes are the interface's business
    public string Declaration() {
        var prefix = IsOut ? "out " : IsRef ? "ref " : string.Empty;
        return $"{prefix}{TypeText} {Identifier}";
    }
}
