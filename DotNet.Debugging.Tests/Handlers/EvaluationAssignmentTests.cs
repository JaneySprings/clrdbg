using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Assignments and calls with values the expression itself builds. A struct or a nullable is constructed in place by
// the IL ('ldloca temp; call .ctor'), and a temporary of the expression has no debuggee storage to construct into
public class EvaluationAssignmentTests : BaseDebugTestFixture {
    public EvaluationAssignmentTests() : base(nameof(EvaluationAssignmentTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var local = 1;
        var item = new SampleClass();
        var box = new SampleStruct { Width = 3 };
        Console.WriteLine($"{local} {item.Number} {box.Width}"); // marker:stop

        public struct SampleStruct {
            public double Width { get; set; }
            public SampleStruct(double width) { Width = width; }
        }
        public class SampleClass {
            public int Number { get; set; } = 7;
            public int Field = 5;
            public double? Nullable { get; set; }
            public SampleStruct Box { get; set; }
            public SampleStruct BoxField;
            public double TakeStruct(SampleStruct value) => value.Width;
            public double TakeNullable(double? value) => value ?? -1;
            public int TakeInt(int value) => value;
        }
        """;
    }

    [Test]
    public void AssignToLocalTest() {
        var threadId = LaunchToMarker();
        var result = Evaluate("local = 99", threadId);
        Assert.That(result.Result, Is.EqualTo("99"));
        Assert.That(Evaluate("local", threadId).Result, Is.EqualTo("99"));
    }

    [Test]
    public void AssignToPropertyTest() {
        var threadId = LaunchToMarker();
        var result = Evaluate("item.Number = 42", threadId);
        Assert.That(result.Result, Is.EqualTo("42"));
        Assert.That(Evaluate("item.Number", threadId).Result, Is.EqualTo("42"));
    }

    [Test]
    public void AssignToFieldTest() {
        var threadId = LaunchToMarker();
        var result = Evaluate("item.Field = 11", threadId);
        Assert.That(result.Result, Is.EqualTo("11"));
        Assert.That(Evaluate("item.Field", threadId).Result, Is.EqualTo("11"));
    }

    [Test]
    public void AssignNullablePropertyTest() {
        var threadId = LaunchToMarker();
        var result = Evaluate("item.Nullable = 44.0", threadId);
        Assert.That(result.Result, Is.EqualTo("44"));
        Assert.That(Evaluate("item.Nullable", threadId).Result, Is.EqualTo("44"));
    }

    [Test]
    public void AssignStructPropertyTest() {
        var threadId = LaunchToMarker();
        var result = Evaluate("item.Box = new SampleStruct(7)", threadId);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(Evaluate("item.Box.Width", threadId).Result, Is.EqualTo("7"));
    }

    [Test]
    public void PassStructArgumentTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("item.TakeStruct(new SampleStruct(9))", threadId).Result, Is.EqualTo("9"));
    }

    [Test]
    public void PassNullableArgumentTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("item.TakeNullable(5.0)", threadId).Result, Is.EqualTo("5"));
    }

    [Test]
    public void PassIntArgumentTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("item.TakeInt(3)", threadId).Result, Is.EqualTo("3"));
    }

    [Test]
    public void AssignStructFieldTest() {
        var threadId = LaunchToMarker();
        Evaluate("item.BoxField = new SampleStruct(8)", threadId);
        Assert.That(Evaluate("item.BoxField.Width", threadId).Result, Is.EqualTo("8"));
    }

    [Test]
    public void MemberOfAStructTemporaryTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("new SampleStruct(7).Width", threadId).Result, Is.EqualTo("7"));
    }
}
