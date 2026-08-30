using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class EvaluationCornerTests : BaseDebugTestFixture {
    public EvaluationCornerTests() : base(nameof(EvaluationCornerTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var count = 42;
        var title = "hello";
        var numbers = new[] { 3, 1, 2 };
        var box = new Box(7);
        Console.WriteLine($"{count}{title}{numbers.Length}{box.Value}"); // marker:stop

        public class Box {
            public int Value;

            public Box(int value) {
                Value = value;
            }
            public int Twice(int extra) {
                return Value * 2 + extra;
            }
        }

        public static class Untouched {
            public static int Marker = 5;
        }
        """;
    }

    [Test]
    public void InterpolatedStringTest() {
        var threadId = LaunchToMarker();
        var result = Evaluate("$\"{title} {count + 1}\"", threadId);
        Assert.That(result.Result, Is.EqualTo("\"hello 43\""), "The interpolation handler is lowered to run on the host");
    }

    [Test]
    public void StringConcatTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("title + \"!\"", threadId).Result, Is.EqualTo("\"hello!\""));
        Assert.That(Evaluate("title.Length", threadId).Result, Is.EqualTo("5"));
    }

    [Test]
    public void NewObjectTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("new Box(5).Value", threadId).Result, Is.EqualTo("5"), "Constructors run in the debuggee");
        Assert.That(Evaluate("new Box(5).Twice(1)", threadId).Result, Is.EqualTo("11"));
    }

    [Test]
    public void CastsTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("(double)count / 4", threadId).Result, Is.EqualTo("10.5"));
        Assert.That(Evaluate("(byte)count", threadId).Result, Is.EqualTo("42"));
        Assert.That(Evaluate("(object)title is string", threadId).Result, Is.EqualTo("true"), "isinst checks run against the debuggee type");
    }

    [Test]
    public void ArrayExpressionsTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("numbers[1] + numbers.Length", threadId).Result, Is.EqualTo("4"));
        Assert.That(Evaluate("new int[5].Length", threadId).Result, Is.EqualTo("5"), "Arrays can be allocated by the evaluation");
    }

    // 'out var' declares a synthetic variable, stored in a single-element array allocated in the debuggee
    [Test]
    public void OutVariableTest() {
        var threadId = LaunchToMarker();
        var result = Evaluate("int.TryParse(\"7\", out var parsed) ? parsed : -1", threadId);
        Assert.That(result.Result, Is.EqualTo("7"));
    }

    [Test]
    public void AssignmentTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("count = count + 1", threadId).Result, Is.EqualTo("43"));
        Assert.That(Evaluate("count", threadId).Result, Is.EqualTo("43"), "The assignment wrote through to the debuggee local");

        Assert.That(Evaluate("box.Value = 70", threadId).Result, Is.EqualTo("70"));
        Assert.That(Evaluate("box.Value", threadId).Result, Is.EqualTo("70"), "The assignment wrote through to the object field");
    }

    [Test]
    public void TernaryTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("count > 40 ? title : \"small\"", threadId).Result, Is.EqualTo("\"hello\""));
    }

    [Test]
    public void TypeofTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("typeof(string).Name", threadId).Result, Is.EqualTo("\"String\""), "typeof resolves through GetTypeFromHandle");
    }

    // The program never touches the type, so reading its static first runs the class initializer in the debuggee
    [Test]
    public void StaticFieldOfNotLoadedTypeTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("Untouched.Marker", threadId).Result, Is.EqualTo("5"));
    }

    [Test]
    public void StaticMemberAccessTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("int.MaxValue", threadId).Result, Is.EqualTo("2147483647"));
        Assert.That(Evaluate("string.IsNullOrEmpty(title)", threadId).Result, Is.EqualTo("false"));
    }
}
