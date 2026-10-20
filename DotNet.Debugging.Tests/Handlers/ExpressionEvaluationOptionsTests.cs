using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// 'expressionEvaluationOptions' of the launch configuration: with implicit function evaluation off no code of the
// debuggee runs on the debugger's own initiative - a listed property becomes a lazy variable the client evaluates on
// request, a display fragment that needs a call says so in its place, ToString and DebuggerTypeProxy are left out,
// and an expression making a call fails; 'allowToString' alone leaves ToString out
public class ExpressionEvaluationOptionsTests : BaseDebugTestFixture {
    private const string CallRefused = "Running code in the program is disabled by the debug configuration";

    public ExpressionEvaluationOptionsTests() : base(nameof(ExpressionEvaluationOptionsTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Diagnostics;

        var holder = new Holder();
        var ticket = new Ticket();
        var reader = new Reader();
        var proxied = new Proxied();
        var numbers = new List<int> { 1, 2, 3 };
        var point = new Point(1, 2);
        var nested = new Nested();
        Console.WriteLine("stop"); // marker:stop
        Console.WriteLine($"{holder}{ticket}{reader.Total}{proxied}{numbers.Count}{point}{nested}");

        [DebuggerDisplay("Count={Count} Field={Field}")]
        class Holder {
            public int Field = 7;
            public int Count { get { return 3; } }
        }
        class Ticket {
            public int Number = 42;
            public override string ToString() { return "ticket:" + Number; }
        }
        class Reader {
            public int Calls;
            public int Auto { get; set; } = 5;
            public int Total { get { return Auto * 2; } }
            public int Counted { get { Calls++; return Calls; } }
        }
        [DebuggerTypeProxy(typeof(ProxiedView))]
        class Proxied {
            public int[] Items = { 1, 2 };
            public int Hidden = 9;
        }
        class ProxiedView {
            private readonly Proxied proxied;
            public ProxiedView(Proxied proxied) { this.proxied = proxied; }
            public int Length { get { return proxied.Items.Length; } }
        }
        struct Point {
            public int X;
            public int Y;
            public Point(int x, int y) { X = x; Y = y; }
            public override string ToString() { return "(" + X + ", " + Y + ")"; }
        }
        class Nested {
            public Ticket Ticket = new Ticket();
            public Holder Holder = new Holder();
        }
        """;
    }

    private static Dictionary<string, JToken> Options(object options) {
        return new Dictionary<string, JToken> { ["expressionEvaluationOptions"] = JObject.FromObject(options) };
    }
    private Dictionary<string, Variable> GetLocals(int threadId) {
        return GetLocalVariables(threadId).ToDictionary(it => it.Name.Split(' ')[0], it => it);
    }
    private List<Variable> GetMembers(Variable variable) {
        return GetVariables(variable.VariablesReference);
    }

    [Test]
    public void ImplicitEvaluationOffListsPropertiesLazilyTest() {
        var threadId = LaunchToMarker(properties: Options(new { allowImplicitFuncEval = false }));
        var locals = GetLocals(threadId);
        Assert.That(locals["ticket"].Value, Is.EqualTo("{Ticket}"), "ToString is not called");
        Assert.That(locals["point"].Value, Is.EqualTo("{Point}"));
        Assert.That(locals["holder"].Value, Is.EqualTo($"Count={CallRefused} Field=7"), "A display renders, the fragment needing a call says so in its place");

        var reader = GetMembers(locals["reader"]);
        var total = reader.First(it => it.Name == "Total [int]");
        Assert.That(total.Value, Is.Empty, "The property is listed without its value");
        Assert.That(total.PresentationHint?.Lazy, Is.True, "The client evaluates it on request");
        Assert.That(total.PresentationHint?.Kind, Is.EqualTo(VariablePresentationHint.KindValue.Property));
        Assert.That(total.EvaluateName, Is.EqualTo("reader.Total"));
        Assert.That(reader.First(it => it.Name == "Calls [int]").Value, Is.EqualTo("0"), "A field reads as usual, and no getter ran: 'Counted' did not count");

        var evaluated = GetVariables(total.VariablesReference);
        Assert.That(evaluated.Select(it => (it.Name, it.Value)), Is.EqualTo(new[] { ("Total [int]", "10") }), "The reference yields the evaluated property alone");
        Assert.That(evaluated[0].PresentationHint?.Lazy, Is.Not.True);

        Assert.That(GetMembers(locals["proxied"]).Select(it => it.Name), Is.EqualTo(new[] { "Hidden [int]", "Items [int[]]" }), "No proxy is constructed, the members themselves are listed");
        var numbers = GetMembers(locals["numbers"]).Select(it => it.Name).ToList();
        Assert.That(numbers, Does.Contain("Count [int]"));
        Assert.That(numbers, Does.Not.Contain("Results View"), "An enumeration would run in the debuggee");
    }

    [Test]
    public void ImplicitEvaluationOffRefusesCallsInExpressionsTest() {
        var threadId = LaunchToMarker(properties: Options(new { allowImplicitFuncEval = false }));
        Assert.That(Evaluate("ticket.Number", threadId).Result, Is.EqualTo("42"), "A read needs no call");
        Assert.That(Evaluate("ticket", threadId).Result, Is.EqualTo("{Ticket}"));
        var error = Assert.Throws<ProtocolException>(() => Evaluate("reader.Total", threadId));
        Assert.That(error!.Message, Is.EqualTo(CallRefused));
        Assert.That(Assert.Throws<ProtocolException>(() => Evaluate("new Ticket()", threadId))!.Message, Is.EqualTo(CallRefused), "A constructor is a call");
    }

    [Test]
    public void ToStringOffShowsTheTypeNameTest() {
        var threadId = LaunchToMarker(properties: Options(new { allowToString = false }));
        var locals = GetLocals(threadId);
        Assert.That(locals["ticket"].Value, Is.EqualTo("{Ticket}"));
        Assert.That(locals["point"].Value, Is.EqualTo("{Point}"));
        Assert.That(locals["holder"].Value, Is.EqualTo("Count=3 Field=7"), "A DebuggerDisplay still renders");
        Assert.That(GetMembers(locals["nested"]).First(it => it.Name == "Ticket [Ticket]").Value, Is.EqualTo("{Ticket}"));
        Assert.That(GetMembers(locals["reader"]).First(it => it.Name == "Total [int]").Value, Is.EqualTo("10"), "Getters still run");
        Assert.That(Evaluate("ticket", threadId).Result, Is.EqualTo("{Ticket}"));
    }

    // A property the block does not have is ignored, the launch configuration still works
    [Test]
    public void UnknownOptionsAreIgnoredTest() {
        var threadId = LaunchToMarker(properties: Options(new { somethingElse = true }));
        var locals = GetLocals(threadId);
        Assert.That(locals["holder"].Value, Is.EqualTo("Count=3 Field=7"));
        Assert.That(GetMembers(locals["proxied"]).Select(it => it.Name), Is.EqualTo(new[] { "Length [int]", "Raw View" }), "The proxy stands in as usual");
    }
}
