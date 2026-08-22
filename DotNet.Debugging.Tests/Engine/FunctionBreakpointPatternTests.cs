using System.Collections.Immutable;
using DotNet.Debugging.Engine.Breakpoints;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class FunctionBreakpointPatternTests {
    private static readonly string[] expectedParameters = ["System.Int32", "System.String", "List`1<System.Nullable`1<System.Int32>>"];

    [Test]
    public void MethodNameOnlyTest() {
        var pattern = FunctionBreakpointPattern.Parse("Main");
        Assert.That(pattern.TypeName, Is.Null);
        Assert.That(pattern.MethodName, Is.EqualTo("Main"));
        Assert.That(pattern.MethodArity, Is.Null);
        Assert.That(pattern.Parameters, Is.Null);
        Assert.That(pattern.MatchesType("Any.Type"), Is.True);
    }

    [Test]
    public void QualifiedNameTest() {
        var pattern = FunctionBreakpointPattern.Parse(" My.Namespace.Program.Main ");
        Assert.That(pattern.TypeName, Is.EqualTo("My.Namespace.Program"));
        Assert.That(pattern.MethodName, Is.EqualTo("Main"));
        Assert.That(pattern.MatchesType("My.Namespace.Program"), Is.True);
        Assert.That(pattern.MatchesType("Other.My.Namespace.Program"), Is.True, "A type name may be given without its leading namespaces");
        Assert.That(pattern.MatchesType("My.Namespace.Program2"), Is.False);
    }

    [Test]
    public void GenericTypeAndMethodTest() {
        var pattern = FunctionBreakpointPattern.Parse("Repository<T>.Find<TKey, TValue>");
        Assert.That(pattern.TypeName, Is.EqualTo("Repository`1"));
        Assert.That(pattern.MethodName, Is.EqualTo("Find"));
        Assert.That(pattern.MethodArity, Is.EqualTo(2));
    }

    [Test]
    public void ParametersTest() {
        var pattern = FunctionBreakpointPattern.Parse("Program.Add(int, System.String, List<int?>)");
        Assert.That(pattern.Parameters, Is.Not.Null);
        Assert.That(pattern.Parameters!.Value, Is.EqualTo(expectedParameters));
        Assert.That(pattern.MatchesParameters(ImmutableArray.Create("System.Int32", "System.String", "System.Collections.Generic.List`1<System.Nullable`1<System.Int32>>")), Is.True);
        Assert.That(pattern.MatchesParameters(ImmutableArray.Create("System.Int32", "System.String")), Is.False);

        var empty = FunctionBreakpointPattern.Parse("Program.Main()");
        Assert.That(empty.Parameters!.Value, Is.Empty);
        Assert.That(empty.MatchesParameters(ImmutableArray<string>.Empty), Is.True);
        Assert.That(empty.MatchesParameters(ImmutableArray.Create("System.String[]")), Is.False);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Program.Main(")]
    [TestCase("Program.Main(int,)")]
    [TestCase("List<int.Add")]
    [TestCase("Program..Main")]
    public void InvalidPatternTest(string value) {
        Assert.Throws<ArgumentException>(() => FunctionBreakpointPattern.Parse(value));
    }
}
