using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Every '{expression}' of a DebuggerDisplay renders like a variable holding its result, a failing one shows its failure
// in place, Name and Type replace a member's name and the type name. The rules were recorded from Microsoft's debugger
public class DebuggerDisplayTests : BaseDebugTestFixture {
    public DebuggerDisplayTests() : base(nameof(DebuggerDisplayTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Diagnostics;

        var reading = new Reading(21.5m, "C");
        var quoted = new QuotedReading(21.5m, "C");
        var tagged = new Tagged();
        var unknown = new Unknown();
        var partlyUnknown = new PartlyUnknown();
        var first = new Node("first") { Next = new Node("last") };
        var last = first.Next!;
        var aliased = new Aliased();
        var faulty = new Faulty();
        var primitives = new Primitives();
        var holder = new Holder<Reading>(new Reading(-4m, "F"));
        var heldTicket = new Holder<Ticket>(new Ticket());
        var heldText = new Holder<string>("note");
        var heldNull = new Holder<Reading?>(null);
        var byCode = new Dictionary<string, Pair> { ["alpha"] = new Pair(1, 2) };
        var lazy = new Lazy<int>(() => 7);
        _ = lazy.Value;
        Console.WriteLine("stop"); // marker:stop
        GC.KeepAlive(new object[] { reading, quoted, tagged, unknown, partlyUnknown, first, last, aliased, faulty, primitives, holder, heldTicket, heldText, heldNull, byCode, lazy });

        [DebuggerDisplay("{Degrees} {Scale,nq}")]
        sealed class Reading {
            public decimal Degrees;
            public string Scale;
            public Reading(decimal degrees, string scale) { Degrees = degrees; Scale = scale; }
        }
        [DebuggerDisplay("{Degrees} {Scale}")]
        sealed class QuotedReading {
            public decimal Degrees;
            public string Scale;
            public QuotedReading(decimal degrees, string scale) { Degrees = degrees; Scale = scale; }
        }
        [DebuggerDisplay("\\{{Level}\\}")]
        sealed class Tagged { public int Level = 9; }
        [DebuggerDisplay("{Absent}")]
        sealed class Unknown { public int Level = 2; }
        [DebuggerDisplay("Level={Level} Extra={Absent}")]
        sealed class PartlyUnknown { public int Level = 4; }
        [DebuggerDisplay("{_label,nq} -> {Next._label,nq}")]
        sealed class Node {
            private string _label;
            public Node? Next;
            public Node(string label) { _label = label; }
        }
        [DebuggerDisplay("{Count}", Name = "alias", Type = "Renamed")]
        sealed class Aliased { public int Count = 4; }
        [DebuggerDisplay("{Broken}")]
        sealed class Faulty { public int Broken => throw new NotSupportedException("no display"); }
        [DebuggerDisplay("{Enabled} {Total} {Label} {Symbol} {Ratio} {Day}")]
        sealed class Primitives {
            public bool Enabled = false;
            public int Total = 12;
            public string Label = "twelve";
            public char Symbol = '#';
            public double Ratio = 0.25;
            public DayOfWeek Day = DayOfWeek.Monday;
        }
        [DebuggerDisplay("[{Left}; {Right}]")]
        readonly struct Pair {
            public int Left { get; }
            public int Right { get; }
            public Pair(int left, int right) { Left = left; Right = right; }
        }
        [DebuggerDisplay("holds {Item}")]
        sealed class Holder<T> {
            public T Item;
            public Holder(T item) { Item = item; }
        }
        sealed class Ticket {
            public int Number = 42;
            public override string ToString() => "ticket:" + Number;
        }
        """;
    }

    private Dictionary<string, Variable> GetLocals(int threadId) {
        return GetLocalVariables(threadId).ToDictionary(it => it.Name.Split(' ')[0], it => it);
    }

    [Test]
    public void StringFragmentsAreQuotedUnlessNoQuotesTest() {
        var locals = GetLocals(LaunchToMarker());
        Assert.That(locals["reading"].Value, Is.EqualTo("21.5 C"), "',nq' shows the string bare, the decimal is invariant");
        Assert.That(locals["quoted"].Value, Is.EqualTo("21.5 \"C\""), "A string fragment is quoted by default");
    }

    [Test]
    public void FragmentsRenderLikeVariablesTest() {
        var locals = GetLocals(LaunchToMarker());
        Assert.That(locals["primitives"].Value, Is.EqualTo("false 12 \"twelve\" 35 '#' 0.25 Monday"));
    }

    [Test]
    public void EscapedBracesAreLiteralTest() {
        var locals = GetLocals(LaunchToMarker());
        Assert.That(locals["tagged"].Value, Is.EqualTo("{9}"));
    }

    [Test]
    public void FailingFragmentShowsItsErrorInPlaceTest() {
        var locals = GetLocals(LaunchToMarker());
        Assert.That(locals["unknown"].Value, Is.EqualTo("error CS0103: The name 'Absent' does not exist in the current context"));
        Assert.That(locals["unknown"].PresentationHint?.Attributes, Is.Null, "A display that fails is not a failed evaluation");
        Assert.That(locals["partlyUnknown"].Value, Is.EqualTo("Level=4 Extra=error CS0103: The name 'Absent' does not exist in the current context"), "The fragments that resolve still render");
    }

    [Test]
    public void NullOnFragmentPathShowsTheExceptionInPlaceTest() {
        var locals = GetLocals(LaunchToMarker());
        Assert.That(locals["first"].Value, Is.EqualTo("first -> last"));
        Assert.That(locals["last"].Value, Is.EqualTo("last -> {System.NullReferenceException: Object reference not set to an instance of an object.}"));
    }

    [Test]
    public void ThrowingFragmentShowsTheExceptionTest() {
        var locals = GetLocals(LaunchToMarker());
        Assert.That(locals["faulty"].Value, Does.StartWith($"{{System.NotSupportedException: no display{Environment.NewLine}   at Faulty.get_Broken() in "));
        Assert.That(locals["faulty"].Value, Does.EndWith("}"));
    }

    [Test]
    public void TypeArgumentReplacesTheTypeAndNameArgumentLeavesALocalAloneTest() {
        var aliased = GetLocals(LaunchToMarker())["aliased"];
        Assert.That(aliased.Name, Is.EqualTo("aliased [Renamed]"));
        Assert.That(aliased.Type, Is.EqualTo("Renamed"));
        Assert.That(aliased.Value, Is.EqualTo("4"), "'Name' never becomes part of the value");
    }

    [Test]
    public void NestedValuesRenderThroughTheirOwnDisplayTest() {
        var locals = GetLocals(LaunchToMarker());
        Assert.That(locals["holder"].Value, Is.EqualTo("holds -4 F"), "The nested value's DebuggerDisplay");
        Assert.That(locals["heldTicket"].Value, Is.EqualTo("holds {ticket:42}"), "The nested value's ToString, in braces");
        Assert.That(locals["heldText"].Value, Is.EqualTo("holds \"note\""));
        Assert.That(locals["heldNull"].Value, Is.EqualTo("holds null"));
        Assert.That(locals["lazy"].Value, Is.EqualTo("ThreadSafetyMode = null, IsValueCreated = true, IsValueFaulted = false, Value = 7"), "The core library's display renders the same way");
    }

    // The dictionary's debug view names its entries through 'Name = "[{Key}]"': the attribute names a member, not a local,
    // and its fragments render like the value's (the string key quoted). Not yet recorded from Microsoft's debugger
    [Test]
    public void NameArgumentNamesAMemberTest() {
        var threadId = LaunchToMarker();
        var byCode = GetLocals(threadId)["byCode"];
        var entries = GetVariables(byCode.VariablesReference);
        var alpha = entries.First(it => it.Name.StartsWith("[\"alpha\"]"));
        Assert.That(alpha.Value, Is.EqualTo("[1; 2]"), "The entry shows its value's own display");
        Assert.That(alpha.EvaluateName, Does.EndWith("Items[0]"), "The expression still addresses the entry by its index");
    }
}
