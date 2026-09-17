using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DotNet.Debugging.Remote.Generator;

// A proxy class of the host library: a [GeneratedComClass] partial class deriving from RemoteObject. The generator
// implements the CorApi interfaces of its base list, except the members written by hand in the class or a base class
internal sealed class ProxyClass {
    private const string RemoteObjectName = "DotNet.Debugging.Remote.Proxy.RemoteObject";

    public INamedTypeSymbol Symbol { get; }
    public string Name => Symbol.Name;
    // Every CorApi interface the class implements, a base before the interfaces deriving from it, each once
    public List<CorApiInterface> Interfaces { get; }
    public bool HasConstructor { get; }

    public ProxyClass(INamedTypeSymbol symbol, Dictionary<string, CorApiInterface> interfaces) {
        Symbol = symbol;
        Interfaces = OrderInterfaces(symbol, interfaces);
        HasConstructor = symbol.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared);
    }

    public static bool IsProxy(INamedTypeSymbol symbol) {
        for (var type = symbol.BaseType; type != null; type = type.BaseType) {
            if (type.ToDisplayString() == RemoteObjectName)
                return true;
        }
        return false;
    }
    // A member of the class or a base class already implements the interface method
    public bool Implements(CorApiInterface iface, CorApiMethod method) {
        var interfaceSymbol = Symbol.AllInterfaces.FirstOrDefault(i => i.Name == iface.Name);
        if (interfaceSymbol == null)
            return false;
        var methodSymbol = interfaceSymbol.GetMembers(method.Name).OfType<IMethodSymbol>().FirstOrDefault(m => m.Parameters.Length == method.Parameters.Count);
        return methodSymbol != null && Symbol.FindImplementationForInterfaceMember(methodSymbol) != null;
    }

    private static List<CorApiInterface> OrderInterfaces(INamedTypeSymbol symbol, Dictionary<string, CorApiInterface> interfaces) {
        var result = new List<CorApiInterface>();
        foreach (var declared in symbol.Interfaces) {
            if (!interfaces.TryGetValue(declared.Name, out var iface))
                continue;
            var chain = iface.BaseChain(interfaces);
            chain.Reverse();
            chain.Add(iface);
            foreach (var entry in chain) {
                if (!result.Contains(entry))
                    result.Add(entry);
            }
        }
        return result;
    }
}
