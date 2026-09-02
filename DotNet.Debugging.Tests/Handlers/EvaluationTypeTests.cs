using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class EvaluationTypeTests : BaseDebugTestFixture {
    public EvaluationTypeTests() : base(nameof(EvaluationTypeTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        uint unsignedMax = uint.MaxValue;
        ulong hugeValue = ulong.MaxValue;
        var flag = false;
        var boxedNumber = (object)5;
        object? nothing = null;
        var kind = Kind.Negative;
        var size = Size.Large;
        var pointer = (nint)42;
        var counter = 10;
        Bump(ref counter);
        Console.WriteLine($"{unsignedMax} {hugeValue} {flag} {boxedNumber} {nothing} {kind} {size} {pointer} {counter}"); // marker:stop

        static void Bump(ref int value) {
            value += 1;
            Console.WriteLine(value); // marker:byref
        }

        public enum Kind : sbyte { Negative = -1, Positive = 1 }
        public enum Size : ushort { Small = 1, Large = 40000 }
        public static class Outer {
            public enum Level { Low, High }
            public static string Describe(Level level) => level.ToString();
        }
        """;
    }

    [Test]
    public void UnsignedValuesWrapLikeTheRuntimeTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("unsignedMax", threadId).Result, Is.EqualTo("4294967295"));
        Assert.That(Evaluate("unsignedMax - 1", threadId).Result, Is.EqualTo("4294967294"));
        Assert.That(Evaluate("hugeValue", threadId).Result, Is.EqualTo("18446744073709551615"));
        Assert.That(Evaluate("hugeValue - 1", threadId).Result, Is.EqualTo("18446744073709551614"));
    }

    [Test]
    public void BoolAssignmentTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("flag = true", threadId).Result, Is.EqualTo("true"));
        Assert.That(Evaluate("flag", threadId).Result, Is.EqualTo("true"));
        Assert.That(Evaluate("flag = unsignedMax > 5", threadId).Result, Is.EqualTo("true"));
    }

    [Test]
    public void NullableUnboxTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("(int)boxedNumber", threadId).Result, Is.EqualTo("5"));
        Assert.That(Evaluate("(int?)boxedNumber", threadId).Result, Is.EqualTo("5"));
        Assert.That(Evaluate("((int?)boxedNumber).Value + 1", threadId).Result, Is.EqualTo("6"));
        Assert.That(Evaluate("(int?)nothing", threadId).Result, Is.EqualTo("null"));
        Assert.That(Evaluate("((int?)nothing).HasValue", threadId).Result, Is.EqualTo("false"));
    }

    [Test]
    public void EnumComparisonHonoursTheUnderlyingTypeTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("kind == Kind.Negative", threadId).Result, Is.EqualTo("true"));
        Assert.That(Evaluate("(int)kind", threadId).Result, Is.EqualTo("-1"));
        Assert.That(Evaluate("size == Size.Large", threadId).Result, Is.EqualTo("true"));
        Assert.That(Evaluate("(int)size", threadId).Result, Is.EqualTo("40000"));
    }

    [Test]
    public void NativeIntegerTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("pointer", threadId).Result, Is.EqualTo("42"));
        Assert.That(Evaluate("pointer + 1", threadId).Result, Is.EqualTo("43"));
    }

    [Test]
    public void NestedTypeInSignatureTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("Outer.Describe(Outer.Level.High)", threadId).Result, Does.Contain("High"));
    }

    [Test]
    public void ByRefParameterTest() {
        var threadId = LaunchToMarker("marker:byref");
        Assert.That(Evaluate("value", threadId).Result, Is.EqualTo("11"));
        Assert.That(Evaluate("value + 1", threadId).Result, Is.EqualTo("12"));
        Assert.That(Evaluate("value = 20", threadId).Result, Is.EqualTo("20"));
        Assert.That(Evaluate("value", threadId).Result, Is.EqualTo("20"));
    }
}
