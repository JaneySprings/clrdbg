using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A member declared on a generic base type is invoked with the base type's arguments, not the value's own.
// A non-generic type deriving from a generic base has none of its own, so taking them from the value made the
// runtime reject the call ('used with the wrong number of generic arguments', shown as a TypeLoadException value)
public class GenericBaseMemberTests : BaseDebugTestFixture {
    public GenericBaseMemberTests() : base(nameof(GenericBaseMemberTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var rows = new RowCollection();
        var pairs = new PairList();
        var closed = new ClosedOverInt();
        Console.WriteLine($"{rows.Count}{pairs.Count}{closed.Count}"); // marker:stop
        Console.WriteLine("done");

        public class CollectionBase<T> {
            protected readonly List<T> items = new List<T>();

            public int Count => items.Count;
            public T First => items[0];
            public static string Kind => "collection";
        }

        public class PairBase<TKey, TValue> : CollectionBase<TKey> {
            public TValue Extra { get; set; } = default!;
        }

        // No type arguments of its own, its members all come from the generic base
        public class RowCollection : CollectionBase<Row> {
            public RowCollection() {
                items.Add(new Row("first"));
                items.Add(new Row("second"));
            }
        }

        // Two levels of generic bases with different arity
        public class PairList : PairBase<Row, int> {
            public PairList() {
                items.Add(new Row("pair"));
                Extra = 42;
            }
        }

        // The derived type is generic too, closing the base over its own argument
        public class ClosedOverInt : CollectionBase<int> {
            public ClosedOverInt() {
                items.Add(7);
            }
        }

        public class Row {
            public string Name;
            public Row(string name) {
                Name = name;
            }
        }
        """;
    }

    [Test]
    public void GenericBasePropertyTest() {
        var threadId = LaunchToMarker();
        var rows = GetLocalVariables(threadId).First(it => it.Name.StartsWith("rows"));

        var members = GetVariables(rows.VariablesReference);
        var count = members.First(it => it.Name.StartsWith("Count"));
        var first = members.First(it => it.Name.StartsWith("First"));
        Assert.That(count.Value, Is.EqualTo("2"), "A getter on the generic base is invoked with the base type's arguments");
        Assert.That(first.Value, Is.EqualTo("{Row}"), "A getter returning the base type's parameter is read through the same call");
    }

    [Test]
    public void GenericBaseStaticPropertyTest() {
        var threadId = LaunchToMarker();
        var rows = GetLocalVariables(threadId).First(it => it.Name.StartsWith("rows"));

        var staticGroup = GetVariables(rows.VariablesReference).First(it => it.Name == "Static members");
        var kind = GetVariables(staticGroup.VariablesReference).First(it => it.Name.StartsWith("Kind"));
        Assert.That(kind.Value, Is.EqualTo("\"collection\""), "A static getter on the generic base needs the base type's arguments too");
    }

    [Test]
    public void NestedGenericBasePropertyTest() {
        var threadId = LaunchToMarker();
        var pairs = GetLocalVariables(threadId).First(it => it.Name.StartsWith("pairs"));

        var members = GetVariables(pairs.VariablesReference);
        Assert.That(members.First(it => it.Name.StartsWith("Extra")).Value, Is.EqualTo("42"), "The two-argument base is invoked with its own two arguments");
        Assert.That(members.First(it => it.Name.StartsWith("Count")).Value, Is.EqualTo("1"), "The one-argument base above it is invoked with its own single argument");
    }

    [Test]
    public void GenericDerivedPropertyTest() {
        var threadId = LaunchToMarker();
        var closed = GetLocalVariables(threadId).First(it => it.Name.StartsWith("closed"));

        var members = GetVariables(closed.VariablesReference);
        Assert.That(members.First(it => it.Name.StartsWith("Count")).Value, Is.EqualTo("1"));
        Assert.That(members.First(it => it.Name.StartsWith("First")).Value, Is.EqualTo("7"), "The base is closed over the argument the derived type passes it");
    }

    [Test]
    public void EvaluateGenericBasePropertyTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("rows.Count", threadId).Result, Is.EqualTo("2"), "The same call is made when the property is evaluated by name");
        Assert.That(Evaluate("pairs.Extra", threadId).Result, Is.EqualTo("42"));
    }
}
