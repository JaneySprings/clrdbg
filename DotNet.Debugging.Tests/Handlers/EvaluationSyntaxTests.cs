using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The C# syntax an expression may use, one family per test: every expression of a family is asserted on its own,
// so one run reports each form the evaluator gets wrong rather than the first
public class EvaluationSyntaxTests : BaseDebugTestFixture {
    public EvaluationSyntaxTests() : base(nameof(EvaluationSyntaxTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var count = 42;
        var title = "hello";
        var text = "Hello, World";
        var numbers = new[] { 3, 1, 2 };
        var bytes = new byte[] { 1, 2, 3, 4 };
        var matrix = new int[,] { { 1, 2 }, { 3, 4 } };
        var words = new List<string> { "alpha", "beta", "gamma" };
        var map = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 };
        var person = new Person("Ann", 30) { Tags = new[] { "a", "b" } };
        Person? nobody = null;
        int? maybe = 5;
        int? none = null;
        var pair = (Left: 1, Right: "two");
        var circle = new Circle(2.0);
        Shape shape = circle;
        object boxed = 42;
        var point = new Point(1, 2);
        var vector = new Vector(3, 4);
        var date = new DateTime(2024, 1, 15);
        var span = TimeSpan.FromMinutes(90);
        var flags = Options.A | Options.C;
        var guid = Guid.Empty;
        Func<int, int> square = x => x * x;
        var wrapper = new Wrapper<int>(9);
        var total = numbers.Sum();
        Console.WriteLine($"{count}{title}{text}{numbers.Length}{bytes.Length}{matrix[1, 1]}{words.Count}{map.Count}{person.Name}{nobody}{maybe}{none}{pair.Left}{circle.Radius}{shape.Name}{boxed}{point}{vector.X}{date.Year}{span}{flags}{guid}{square(2)}{wrapper.Value}{total}"); // marker:stop
        Console.WriteLine("done");

        public class Person {
            public string Name { get; }
            public int Age { get; set; }
            public string[] Tags { get; set; } = Array.Empty<string>();
            public bool IsAdult => Age >= 18;
            public int this[int index] => Age + index;

            public Person(string name, int age) {
                Name = name;
                Age = age;
            }
            public string Greet(string greeting = "Hi") => $"{greeting}, {Name}";
            public T Convert<T>(Func<Person, T> selector) => selector(this);
            public static Person Create(string name) => new Person(name, 0);
            public override string ToString() => $"{Name} ({Age})";
        }
        public abstract class Shape {
            public abstract double Area { get; }
            public virtual string Name => "Shape";
        }
        public class Circle : Shape {
            public double Radius;
            public override double Area => Math.PI * Radius * Radius;
            public override string Name => "Circle";

            public Circle(double radius) {
                Radius = radius;
            }
        }
        public record Point(int X, int Y);
        public struct Vector {
            public int X;
            public int Y;
            public int Length2 => X * X + Y * Y;

            public Vector(int x, int y) {
                X = x;
                Y = y;
            }
            public static Vector operator +(Vector left, Vector right) => new Vector(left.X + right.X, left.Y + right.Y);
        }
        [Flags]
        public enum Options { None = 0, A = 1, B = 2, C = 4 }
        public class Wrapper<T> {
            public T Value;

            public Wrapper(T value) {
                Value = value;
            }
            public TResult Map<TResult>(Func<T, TResult> selector) => selector(Value);
            public static Wrapper<T> Of(T value) => new Wrapper<T>(value);
        }
        public static class Extensions {
            public static int Doubled(this int value) => value * 2;
            public static string Shout(this string value) => value.ToUpper() + "!";
        }
        """;
    }

    [Test]
    public void ArithmeticAndOperatorsTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("count * 2 + 1", "85"),
            ("count % 5", "2"),
            ("10 / 3", "3"),
            ("10.0 / 3", "3.3333333333333335"),
            ("count / 5.0", "8.4"),
            ("-count", "-42"),
            ("count << 2", "168"),
            ("count >> 1", "21"),
            ("count & 0xF", "10"),
            ("count | 1", "43"),
            ("count ^ 1", "43"),
            ("~count", "-43"),
            ("count >= 42 && title.Length == 5", "true"),
            ("count < 0 || title == \"hello\"", "true"),
            ("!(count < 0)", "true"),
            ("1.5m * 2", "3.0"),
            ("'a' + 1", "98"),
            ("(long)int.MaxValue + 1", "2147483648"),
            ("checked(count * 1000)", "42000"),
            ("unchecked((byte)300)", "44"),
            // Constants fold unchecked: the expression compiler compiles without overflow checks, like Microsoft's debugger
            ("(sbyte)200", "-56"),
            ("count == 42 ? \"yes\" : \"no\"", "\"yes\""),
            ("count > 40 && count < 50 ? count * 2 : 0", "84"));
    }

    [Test]
    public void NullHandlingTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("nobody == null", "true"),
            ("nobody?.Name", "null"),
            ("nobody?.Age ?? -1", "-1"),
            ("person?.Name", "\"Ann\""),
            ("person?.Tags?[0]", "\"a\""),
            ("person!.Name", "\"Ann\""),
            ("maybe ?? 0", "5"),
            ("none ?? 7", "7"),
            ("maybe.HasValue", "true"),
            ("none.HasValue", "false"),
            ("maybe.Value + 1", "6"),
            ("none.GetValueOrDefault()", "0"),
            ("(nobody?.Age).GetValueOrDefault()", "0"),
            ("maybe + 1", "6"),
            ("none + 1", "null"),
            ("maybe == 5", "true"),
            ("none == null", "true"));
    }

    [Test]
    public void StringsTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("title.ToUpper()", "\"HELLO\""),
            ("title + \" \" + text", "\"hello Hello, World\""),
            ("$\"{title}:{count:D4}\"", "\"hello:0042\""),
            ("$\"{count,5}|\"", "\"   42|\""),
            ("$\"{person.Name} is {person.Age}\"", "\"Ann is 30\""),
            ("text.Substring(7)", "\"World\""),
            ("text.Split(',')[1].Trim()", "\"World\""),
            ("string.Join(\"-\", words)", "\"alpha-beta-gamma\""),
            ("text[0]", "72 'H'"),
            ("text.Length > 5 ? \"long\" : \"short\"", "\"long\""),
            ("string.Format(\"{0}-{1}\", count, title)", "\"42-hello\""),
            ("text.Contains(\"World\")", "true"),
            ("text.IndexOf('W')", "7"),
            ("@\"a\\b\"", "\"a\\\\b\""),
            ("\"tab\\tsep\"", "\"tab\\tsep\""),
            ("char.IsUpper(text[0])", "true"),
            ("title.Shout()", "\"HELLO!\""),
            ("count.ToString(\"X\")", "\"2A\""),
            ("string.Empty.Length", "0"),
            ("string.Concat(title, count)", "\"hello42\""),
            ("title.Replace('l', 'L')", "\"heLLo\""),
            ("text.StartsWith(\"Hello\") && text.EndsWith(\"World\")", "true"),
            ("\"\"\"<raw \"quoted\">\"\"\"", "\"<raw \\\"quoted\\\">\""));
    }

    [Test]
    public void CollectionsAndIndexersTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("words[1]", "\"beta\""),
            ("words.Count", "3"),
            ("map[\"two\"]", "2"),
            ("map.ContainsKey(\"one\")", "true"),
            ("map.Keys.Count", "2"),
            ("numbers.Length", "3"),
            ("numbers[^1]", "2"),
            ("bytes[^1]", "4"),
            ("numbers[1..].Length", "2"),
            ("numbers[..2][1]", "1"),
            ("matrix[1, 0]", "3"),
            ("matrix.GetLength(1)", "2"),
            ("new List<int> { 1, 2 }.Count", "2"),
            ("new Dictionary<string, int> { { \"k\", 1 } }[\"k\"]", "1"),
            ("words.ToArray().Length", "3"),
            ("words.Contains(\"beta\")", "true"),
            ("words.IndexOf(\"gamma\")", "2"),
            ("person.Tags.Length", "2"),
            ("person[3]", "33"),
            ("numbers.Sum()", "6"),
            ("numbers.Max()", "3"),
            ("numbers.Min()", "1"),
            ("Enumerable.Range(1, 5).Sum()", "15"),
            ("numbers.Reverse().First()", "2"),
            ("words.Skip(1).First()", "\"beta\""),
            ("numbers.Distinct().Count()", "3"),
            ("Array.Empty<int>().Length", "0"));
    }

    [Test]
    public void MembersAndMethodsTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("person.Name", "\"Ann\""),
            ("person.Greet()", "\"Hi, Ann\""),
            ("person.Greet(\"Yo\")", "\"Yo, Ann\""),
            ("person.Greet(greeting: \"Named\")", "\"Named, Ann\""),
            ("person.IsAdult", "true"),
            ("Person.Create(\"Bob\").Age", "0"),
            ("person.ToString()", "\"Ann (30)\""),
            ("new Person(\"X\", 1).Name", "\"X\""),
            ("new Person(\"Y\", 2) { Age = 9 }.Age", "9"),
            ("circle.Radius", "2"),
            ("circle.Area > 12", "true"),
            ("shape.Name", "\"Circle\""),
            ("shape.Area", "12.566370614359172"),
            ("((Circle)shape).Radius", "2"),
            ("(shape as Circle).Radius", "2"),
            ("shape.GetType().Name", "\"Circle\""),
            ("point.X + point.Y", "3"),
            ("point == new Point(1, 2)", "true"),
            ("point.Equals(new Point(1, 3))", "false"),
            ("point.ToString()", "\"Point { X = 1, Y = 2 }\""),
            ("(point with { X = 10 }).X", "10"),
            ("vector.Length2", "25"),
            ("(vector + new Vector(1, 1)).X", "4"),
            ("new Vector(1, 2).Y", "2"),
            ("count.Doubled()", "84"),
            ("Extensions.Doubled(count)", "84"),
            ("date.AddDays(1).Day", "16"),
            ("date.DayOfWeek", "Monday"),
            ("span.TotalMinutes", "90"),
            ("(TimeSpan.FromHours(1) + span).TotalHours", "2.5"),
            ("Math.Max(count, 100)", "100"),
            ("Math.Sqrt(16)", "4"),
            ("Math.Round(2.5)", "2"),
            ("int.Parse(\"12\") + 1", "13"),
            ("Convert.ToInt32(\"8\")", "8"),
            ("BitConverter.ToInt16(bytes, 0)", "513"),
            ("Guid.Empty == guid", "true"),
            ("Environment.ProcessorCount > 0", "true"));
    }

    [Test]
    public void GenericsAndTypesTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("wrapper.Value", "9"),
            ("Wrapper<int>.Of(3).Value", "3"),
            ("new Wrapper<string>(\"s\").Value", "\"s\""),
            ("new Wrapper<Person>(person).Value.Name", "\"Ann\""),
            ("default(int)", "0"),
            ("default(string)", "null"),
            ("default(Point)", "null"),
            ("typeof(List<int>).Name", "\"List`1\""),
            ("typeof(int).IsValueType", "true"),
            ("typeof(Person).GetProperty(\"Name\").Name", "\"Name\""),
            ("nameof(count)", "\"count\""),
            ("nameof(Person.Name)", "\"Name\""),
            ("sizeof(int)", "4"),
            ("(long)count", "42"),
            ("(double)count / 5", "8.4"),
            ("(int)3.99", "3"),
            ("(int)-3.99", "-3"),
            ("(uint)count", "42"),
            ("(int)'A'", "65"),
            ("(char)65", "65 'A'"),
            ("(object)count", "42"),
            ("(IComparable)count", "42"),
            ("boxed as string", "null"),
            ("(int)boxed", "42"),
            ("(decimal)count / 8", "5.25"),
            ("(float)count / 8", "5.25"),
            ("(int?)null", "null"),
            ("count as int?", "42"),
            ("(long?)count", "42"),
            ("Convert.ToString(count, 2)", "\"101010\""),
            ("unchecked((int)uint.MaxValue)", "-1"),
            ("(Options)6", "B | C"));
    }

    [Test]
    public void PatternsAndSwitchesTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("count is 42", "true"),
            ("count is > 40 and < 50", "true"),
            ("count is not 0", "true"),
            ("title is { Length: 5 }", "true"),
            ("person is { Age: >= 18, Name: \"Ann\" }", "true"),
            ("boxed is not string", "true"),
            ("boxed is int ? (int)boxed + 1 : 0", "43"),
            ("pair is (1, \"two\")", "true"),
            ("numbers is [3, 1, 2]", "true"),
            ("numbers is [3, ..]", "true"),
            ("words is [_, _, _]", "true"),
            ("count switch { < 10 => \"small\", < 100 => \"medium\", _ => \"large\" }", "\"medium\""),
            ("shape switch { Circle disc => disc.Radius, _ => 0 }", "2"),
            ("flags switch { Options.None => 0, _ => 1 }", "1"),
            ("(count, title) switch { (42, \"hello\") => \"both\", _ => \"other\" }", "\"both\""));
    }

    [Test]
    public void TuplesAndEnumsTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("pair.Left", "1"),
            ("pair.Right", "\"two\""),
            ("pair.Item1 + 1", "2"),
            ("(count, title).Item2", "\"hello\""),
            ("(pair.Left + 1, pair.Right).Item1", "2"),
            ("new[] { pair }.Length", "1"),
            ("pair == (1, \"two\")", "true"),
            ("flags.HasFlag(Options.A)", "true"),
            ("(flags & Options.B) == Options.B", "false"),
            ("flags | Options.B", "A | B | C"),
            ("(int)flags", "5"),
            ("Options.C.ToString()", "\"C\""),
            ("Enum.GetName(typeof(Options), Options.B)", "\"B\""),
            ("flags == (Options.A | Options.C)", "true"),
            ("Options.A < Options.B", "true"));
    }

    // A delegate the debuggee holds is invoked in the debuggee like any method; creating one is a different matter (below)
    [Test]
    public void DelegatesTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("square(3)", "9"),
            ("square.Invoke(4)", "16"),
            ("square.Method.Name", "\"<<Main>$>b__0_0\""),
            ("square.Target != null", "true"),
            // A lambda the expression declares is interpreted by the debugger, with the locals it captures
            ("((Func<int, int>)(x => x + 1))(2)", "3"),
            ("new Func<int>(() => 5)()", "5"),
            ("((Func<int, int>)(x => x + count))(1)", "43"),
            ("((Func<string, string>)(s => s + title))(\"a\")", "\"ahello\""),
            ("((Func<int, int, int>)((a, b) => a * b))(6, 7)", "42"),
            // A method group over a debuggee method
            ("((Func<int, int>)Extensions.Doubled)(21)", "42"),
            ("((Func<int, int>)Extensions.Doubled).Invoke(4)", "8"));
    }

    // System.Linq operators handed a lambda run in the debugger: the source is enumerated by the debuggee, the
    // lambda interpreted per element, and a sequence result is shown as an array
    [Test]
    public void LinqTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("numbers.Any(n => n > 2)", "true"),
            ("numbers.All(n => n > 2)", "false"),
            ("numbers.Count(n => n > 1)", "2"),
            ("numbers.Where(n => n > 1).Count()", "2"),
            ("numbers.Where(n => n > 1).Sum()", "5"),
            ("numbers.Select(n => n * 10).Sum()", "60"),
            ("numbers.Sum(n => n * 2)", "12"),
            ("numbers.Max(n => -n)", "-1"),
            ("numbers.Min(n => n * 1.5)", "1.5"),
            ("numbers.Average(n => n)", "2"),
            ("numbers.Aggregate((a, b) => a + b)", "6"),
            ("numbers.Aggregate(10, (a, b) => a + b)", "16"),
            ("numbers.First(n => n < 3)", "1"),
            ("numbers.FirstOrDefault(n => n > 10)", "0"),
            ("numbers.Last(n => n < 3)", "2"),
            ("numbers.Single(n => n == 2)", "2"),
            ("numbers.OrderBy(n => n).First()", "1"),
            ("numbers.OrderByDescending(n => n).ToArray()[0]", "3"),
            ("numbers.OrderBy(n => n).ThenBy(n => -n).Last()", "3"),
            ("numbers.Where(n => n != 1).Contains(3)", "true"),
            ("numbers.Select(n => n + 1).ToList().Count", "3"),
            ("numbers.Select((n, i) => n * i).Sum()", "5"),
            ("numbers.Where(n => n > 1).Skip(1).First()", "2"),
            ("numbers.TakeWhile(n => n > 1).Count()", "1"),
            ("numbers.Where(n => n > 0).Reverse().First()", "2"),
            ("numbers.Select(n => n % 2).Distinct().Count()", "2"),
            ("words.Select(w => w.ToUpper()).First()", "\"ALPHA\""),
            ("words.Where(w => w.Length > 4).Count()", "2"),
            ("words.Select(w => w.Length).Max()", "5"),
            ("words.OrderByDescending(w => w).First()", "\"gamma\""),
            ("words.First(w => w.StartsWith(\"g\"))", "\"gamma\""),
            ("words.Any(w => w == title)", "false"),
            ("string.Join(\",\", words.Select(w => w[0]))", "\"a,b,g\""),
            ("string.Join(\"-\", words.Where(w => w != \"beta\"))", "\"alpha-gamma\""),
            ("map.Where(p => p.Value > 1).Select(p => p.Key).First()", "\"two\""),
            ("person.Tags.Select(t => t + \"!\").Last()", "\"b!\""),
            ("bytes.Select(b => (int)b).Sum()", "10"),
            ("numbers.Select(n => new { Value = n, Twice = n * 2 }).First(v => v.Value == 2).Twice", "4"),
            // Errors the operators throw, reported the way a debuggee exception is (the harness adds its own 'error: ')
            ("numbers.First(n => n > 10)", "error: error: Evaluation threw System.InvalidOperationException"),
            ("numbers.Single(n => n > 1)", "error: error: Evaluation threw System.InvalidOperationException"),
            // A lambda cannot leave the debugger: the debuggee has no code for it
            ("wrapper.Map(v => v + 1)", "error: error: A lambda can be invoked or handed to a System.Linq operator, the debuggee has no code for it"));
    }

    [Test]
    public void AssignmentsTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("count += 8", "50"),
            ("count", "50"),
            ("count++", "50"),
            ("count", "51"),
            ("--count", "50"),
            ("count -= 10", "40"),
            ("count *= 2", "80"),
            ("count /= 4", "20"),
            ("count %= 6", "2"),
            ("count <<= 3", "16"),
            ("count |= 1", "17"),
            ("person.Age = 31", "31"),
            ("person.Age++", "31"),
            ("person.Age", "32"),
            ("person.Age += 8", "40"),
            ("words[0] = \"zeta\"", "\"zeta\""),
            ("words[0]", "\"zeta\""),
            ("numbers[0] += 10", "13"),
            ("numbers[0]", "13"),
            ("map[\"three\"] = 3", "3"),
            ("map.Count", "3"),
            ("person.Tags[1] = \"z\"", "\"z\""),
            ("person.Tags[1]", "\"z\""),
            ("title = title + \"!\"", "\"hello!\""),
            ("title", "\"hello!\""),
            ("maybe = null", "null"),
            ("maybe", "null"),
            ("maybe = 8", "8"),
            ("maybe", "8"),
            ("(maybe = 9).HasValue", "true"),
            ("(maybe = 9).Value", "9"),
            ("maybe ??= 1", "9"),
            ("none ??= 1", "1"),
            ("nobody = person", "{Ann (40)}"),
            ("nobody.Name", "\"Ann\""),
            ("circle.Radius = 3", "3"),
            ("shape.Area > 28", "true"),
            ("vector.X = 5", "5"),
            ("vector.Length2", "41"),
            ("flags = Options.B", "B"),
            ("flags", "B"),
            ("boxed = \"now a string\"", "\"now a string\""),
            ("boxed is string", "true"));
    }

    [Test]
    public void ObjectCreationAndInitializersTest() {
        var threadId = LaunchToMarker();
        AssertEvaluations(threadId,
            ("new Person(\"Z\", 5).Age", "5"),
            ("new Person(\"Z\", 5) { Tags = new[] { \"t\" } }.Tags.Length", "1"),
            ("new List<string> { \"x\", \"y\" }[1]", "\"y\""),
            ("new int[2][] { new[] { 1 }, new[] { 2, 3 } }[1].Length", "2"),
            ("new Point(3, 4).X", "3"),
            ("new Vector(1, 2).Length2", "5"),
            ("new DateTime(2020, 2, 29).DayOfYear", "60"),
            ("new System.Text.StringBuilder().Append(\"a\").Append(1).ToString()", "\"a1\""),
            ("new Wrapper<int>(1) is Wrapper<int>", "true"),
            ("new object() != null", "true"),
            ("new List<int>(numbers).Count", "3"),
            ("new HashSet<int> { 1, 1, 2 }.Count", "2"),
            ("new KeyValuePair<string, int>(\"k\", 7).Value", "7"),
            ("new Exception(\"boom\").Message", "\"boom\""),
            // An array initializer of constants: the data the expression assembly carries is copied into the array
            ("new[] { 1, 2, 3 }.Length", "3"),
            ("new int[] { 1, 2, 3 }[1]", "2"),
            ("new[] { 1.5, 2.5, 3.5 }[2]", "3.5"),
            ("new long[] { 1, 2, 3, 4 }[3]", "4"),
            ("new[] { 'a', 'b', 'c' }[1]", "98 'b'"),
            ("new[] { 1, 2, 3, count }[3]", "42"),
            // A multidimensional array is allocated by the debuggee's Array.CreateInstance
            ("new int[2, 3].Length", "6"),
            ("new int[2, 3].GetLength(1)", "3"),
            ("(new int[2, 3])[1, 2]", "0"),
            // String constructors are built in the debugger, the runtime refuses to run them in a func eval
            ("new string('x', 3)", "\"xxx\""),
            ("new string(new[] { 'a', 'b', 'c' })", "\"abc\""),
            ("new string('-', 2) + title", "\"--hello\""),
            // An anonymous object lives in the debugger, its members are read there
            ("new { Name = \"x\", Value = 1 }.Value", "1"),
            ("new { Name = \"x\", Value = 1 }.Name", "\"x\""),
            ("new { count, title }.title.Length", "5"),
            ("new { Person = person }.Person.Age", "30"));
    }

    // The syntax the evaluator does not support yet, each form reported as an error rather than a wrong value or a
    // hang. A form that starts working fails here and moves to the family it belongs to
    [Test]
    public void UnsupportedSyntaxIsReportedTest() {
        var threadId = LaunchToMarker();
        Assert.Multiple(() => {
            foreach (var expression in new[] {
                // A lambda only exists in the debugger, a debuggee method cannot be handed one
                "wrapper.Map(v => v + 1)",
                "person.Convert(p => p.Name)",
                // An anonymous object only exists in the debugger, it cannot be the result or reach the debuggee
                "new { Name = \"x\", Value = 1 }",
                "new { Name = \"x\" }.ToString()",
                // A variable declared by a pattern is left undeclared by Roslyn's expression compiler (its code generator fails on it)
                "boxed is int n ? n + 1 : 0",
                "shape is Circle c ? c.Radius : 0",
                "numbers is [3, .. var rest] ? rest.Length : -1",
                "count is var any ? any : 0",
            }) {
                Assert.That(EvaluateOrError(expression, threadId), Does.StartWith("error"), expression);
            }
        });
        Assert.That(Evaluate("count", threadId).Result, Is.EqualTo("42"), "The session answers after the failed evaluations");
    }

    // An expression that cannot be evaluated is an error the session survives
    [Test]
    public void ErrorsAreReportedTest() {
        var threadId = LaunchToMarker();
        Assert.Multiple(() => {
            foreach (var expression in new[] {
                "undefinedName",
                "count +",
                "person.Missing",
                "person.Greet(1)",
                "nobody.Name",
                "numbers[10]",
                "int.Parse(\"x\")",
                "(string)boxed",
                "count / (count - 42)",
                "checked((byte)(count * 10))",
                "await Task.FromResult(1)",
                "new Person()",
                "words.Missing()",
                "throw new Exception()",
                "count = \"text\"",
            }) {
                Assert.That(EvaluateOrError(expression, threadId), Does.StartWith("error"), expression);
            }
        });
        Assert.That(Evaluate("count", threadId).Result, Is.EqualTo("42"), "The session answers after the failed evaluations");
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
