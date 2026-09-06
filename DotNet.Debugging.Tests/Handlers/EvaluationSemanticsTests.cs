using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Corners where the interpreter's result must equal the runtime's: NaN in branches, unboxed primitives, stores
// into object slots, nullable boxing, the LINQ emulation's comparisons, interpolation of computed values
public class EvaluationSemanticsTests : BaseDebugTestFixture {
    public EvaluationSemanticsTests() : base(nameof(EvaluationSemanticsTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        double nan = double.NaN;
        int count = -7;
        long longIndex = 1;
        int? maybe = 3;
        int? none = null;
        object boxedFalse = false;
        object boxedLong = 5000000000L;
        object boxedDouble = 2.5;
        object alias = boxedFalse;
        object[] boxes = { 1, 1, 2 };
        int[] numbers = { 3, 1, 2 };
        uint[] unsigned = { uint.MaxValue, 1u };
        string text = "abc";
        char letter = 'x';
        Console.WriteLine($"{nan}{count}{longIndex}{maybe}{none}{boxedFalse}{boxedLong}{boxedDouble}{alias}{boxes.Length}{numbers.Length}{unsigned.Length}{text}{letter}"); // marker:stop
        Console.WriteLine("done");
        """;
    }

    [Test]
    public void NaNComparisonsTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("nan >= 0.0 ? 1 : 2", "2"),
            ("nan <= 0.0 ? 1 : 2", "2"),
            ("nan > 0.0 ? 1 : 2", "2"),
            ("nan < 0.0 ? 1 : 2", "2"),
            ("nan == nan ? 1 : 2", "2"),
            ("nan != nan ? 1 : 2", "1"),
            ("nan >= 0.0 || nan <= 0.0", "false"),
            ("!(nan >= 0.0)", "true"),
            ("count >= -7 ? 1 : 2", "1"),
            ("count <= -8 ? 1 : 2", "2"));
    }

    [Test]
    public void UnboxedPrimitivesTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("(bool)boxedFalse ? \"yes\" : \"no\"", "\"no\""),
            ("(bool)boxedFalse", "false"),
            ("(long)boxedLong + (long)boxedLong", "10000000000"),
            ("(double)boxedDouble * 2", "5"),
            ("(double)boxedDouble > 2", "true"),
            ("(int)(double)boxedDouble", "2"));
    }

    // An object slot takes the new reference; the box it held before belongs to whoever else references it
    [Test]
    public void ObjectSlotAssignmentTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("boxedFalse = true", "true"),
            ("alias", "false"),
            ("boxedFalse", "true"),
            ("boxes[2] = null", "null"),
            ("boxes[2]", "null"),
            ("boxes[1] = 2.5", "2.5"),
            ("boxes[1]", "2.5"),
            ("boxes[0] = 7L", "7"),
            ("boxes[0]", "7"));
    }

    [Test]
    public void NullableBoxingTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("maybe is int", "true"),
            ("(object)maybe is int", "true"),
            ("(object)none == null", "true"),
            ("maybe.GetType().Name", "\"Int32\""));
    }

    [Test]
    public void LinqComparisonsTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("unsigned.Select(x => x).Max()", "4294967295"),
            ("unsigned.OrderBy(x => x).First()", "1"),
            ("numbers.Select(x => (uint)(x - 2)).Min()", "0"),
            ("boxes.Select(o => o).Distinct().Count()", "2"),
            ("boxes.Select(o => o).Contains(2)", "true"),
            ("boxes.Select(o => o).Max()", "2"));
    }

    [Test]
    public void InterpolationOfComputedValuesTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("$\"{count > 1}\"", "\"False\""),
            ("$\"{(char)(count + 127)}\"", "\"x\""),
            ("$\"{typeof(int)}\"", "\"System.Int32\""));
    }

    [Test]
    public void ConversionsAndIndexesTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("numbers[longIndex]", "1"),
            ("new byte[longIndex].Length", "1"));
        Assert.That(EvaluateOrError("checked((ulong)(long)count)", threadId), Does.Contain("OverflowException"));
        Assert.That(EvaluateOrError("numbers[5]", threadId), Does.Contain("IndexOutOfRangeException"));
    }

    // The compiler lowers 'string + char', array literals handed to span APIs and the span extension methods to
    // ReadOnlySpan<T> code; a span cannot cross into the debuggee, the interpreter runs those forms on the host
    [Test]
    public void SpanFormsTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("text + letter", "\"abcx\""),
            ("letter + text", "\"xabc\""),
            ("text + letter + letter", "\"abcxx\""),
            ("letter.ToString() + \"y\"", "\"xy\""),
            ("new[] { 3, 1, 2 }.Contains(1)", "true"),
            ("new[] { 3, 1, 2 }.IndexOf(2)", "2"),
            ("numbers.SequenceEqual(new[] { 3, 1, 2 })", "true"),
            ("numbers.AsSpan()[0]", "3"),
            ("numbers.AsSpan().Length", "3"),
            ("numbers.AsSpan(1).ToArray().Length", "2"),
            ("text.AsSpan()[1]", "98 'b'"),
            ("text.AsSpan(1).ToString()", "\"bc\""),
            ("text.AsSpan().StartsWith(\"ab\")", "true"),
            ("numbers.AsSpan()[2] = 9", "9"),
            ("numbers[2]", "9"));
    }

    // The debuggee has not loaded System.Linq at the stop: the compiler's retry only knows an unknown extension
    // method may be there, the static class name has to be guessed the same way
    [Test]
    public void LinqStaticClassLoadsTheAssemblyTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId, ("Enumerable.Range(1, 3).Sum()", "6"));
    }

    // The qualified form fails on the namespace member instead of the name, a different compiler error
    [Test]
    public void LinqNamespaceLoadsTheAssemblyTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId, ("System.Linq.Enumerable.Repeat(2, 2).Count()", "2"));
    }

    // Enumerable's Min returns NaN as soon as it meets one, Max ignores it unless nothing else is there
    [Test]
    public void NaNInMinMaxTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("new[] { nan, 1.0 }.Select(x => x).Max()", "1"),
            ("new[] { 1.0, nan }.Select(x => x).Min()", "NaN"),
            ("new[] { nan }.Select(x => x).Max()", "NaN"),
            ("new[] { nan, 1.0 }.Max()", "1"),
            ("new[] { nan, 1.0 }.Min()", "NaN"));
    }

    [Test]
    public void PatternVariableIsReportedAsUnsupportedTest() {
        var threadId = LaunchToMarker();
        Assert.That(EvaluateOrError("boxedLong is long value ? value : 0", threadId), Does.Contain("pattern"));
        Assert.That(EvaluateOrError("boxedLong is long", threadId), Is.EqualTo("true"), "A pattern declaring no variable is fine");
    }

    private void AssertEvaluations(int threadId, params (string Expression, string Expected)[] cases) {
        Assert.Multiple(() => {
            foreach (var (expression, expected) in cases) {
                // Evaluated once: an assignment must not run twice
                var actual = EvaluateOrError(expression, threadId);
                Assert.That(actual, Is.EqualTo(expected), $"{expression} => {actual}");
            }
        });
    }
    private string EvaluateOrError(string expression, int threadId) {
        try {
            return Evaluate(expression, threadId).Result;
        }
        catch (ProtocolException ex) {
            return $"error: {ex.Message}";
        }
    }
}
