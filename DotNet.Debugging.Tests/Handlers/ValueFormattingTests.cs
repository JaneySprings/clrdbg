using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class ValueFormattingTests : BaseDebugTestFixture {
    public ValueFormattingTests() : base(nameof(ValueFormattingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var color = Color.Green;
        var access = Access.Read | Access.Execute;
        var unnamed = (Access)8;
        var maybe = (int?)42;
        var nothing = (int?)null;
        var price = 19.99m;
        var letter = 'a';
        object boxed = 123;
        var anonymous = new { Id = 7, Name = "seven" };
        var wrapped = new Wrapped(3);
        var holder = new Holder();
        Guid? identifier = Guid.Empty;
        Point? point = new Point(1, 2);
        int[][] jagged = new int[3][];
        var numbers = new NumberList { 1, 2, 3 };
        var tagged = new Tagged { Value = 5 };
        Console.WriteLine($"{color}{access}{unnamed}{maybe}{nothing}{price}{letter}{boxed}{anonymous}{wrapped}{holder}{identifier}{point}{jagged.Length}{numbers.Count}{tagged}"); // marker:stop

        public enum Color { Red, Green, Blue }
        [Flags]
        public enum Access { None = 0, Read = 1, Write = 2, Execute = 4 }

        [System.Diagnostics.DebuggerDisplay("Wrapped: {Value}")]
        public class Wrapped {
            public int Value;
            [System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.Never)]
            public int Hidden = 99;

            public Wrapped(int value) {
                Value = value;
            }
        }

        public class Holder {
            public static int Counter = 42;
            public const int Limit = 10;
        }
        public struct Point {
            public int X;
            public int Y;

            public Point(int x, int y) {
                X = x;
                Y = y;
            }
        }
        public class NumberList : List<int> { }
        [System.Diagnostics.DebuggerDisplay("Tagged {Value}")]
        public class TaggedBase {
            public int Value;
        }
        public class Tagged : TaggedBase { }
        """;
    }

    [Test]
    public void EnumValuesTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        Assert.That(locals.First(it => it.Name == "color [Color]").Value, Is.EqualTo("Green"));
        Assert.That(locals.First(it => it.Name == "access [Access]").Value, Is.EqualTo("Read | Execute"), "A [Flags] value is decomposed into its members");
        Assert.That(locals.First(it => it.Name == "unnamed [Access]").Value, Is.EqualTo("8"), "A value no members combine into stays numeric");
    }

    [Test]
    public void NullableValuesTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        Assert.That(locals.First(it => it.Name == "maybe [int?]").Value, Is.EqualTo("42"));
        Assert.That(locals.First(it => it.Name == "nothing [int?]").Value, Is.EqualTo("null"));
    }

    [Test]
    public void PrimitiveFormatsTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        Assert.That(locals.First(it => it.Name == "price [decimal]").Value, Is.EqualTo("19.99"));
        Assert.That(locals.First(it => it.Name == "letter [char]").Value, Is.EqualTo("97 'a'"), "Chars show the numeric value and the literal");
        Assert.That(locals.First(it => it.Name == "boxed [int]").Value, Is.EqualTo("123"), "A boxed primitive is shown as the primitive itself");
    }

    // The compiler's display is '\{ Id = {Id}, Name = {Name} }' with Type = "<Anonymous Type>": the escaped braces are
    // literal, a string fragment is quoted like any other, the type argument names the type
    [Test]
    public void AnonymousTypeDisplayTest() {
        var threadId = LaunchToMarker();
        var anonymous = GetLocalVariables(threadId).First(it => it.Name.StartsWith("anonymous"));
        Assert.That(anonymous.Value, Is.EqualTo("{ Id = 7, Name = \"seven\" }"));
        Assert.That(anonymous.Name, Is.EqualTo("anonymous [<Anonymous Type>]"));
        Assert.That(anonymous.Type, Is.EqualTo("<Anonymous Type>"));
    }

    [Test]
    public void DebuggerDisplayAndBrowsableTest() {
        var threadId = LaunchToMarker();
        var wrapped = GetLocalVariables(threadId).First(it => it.Name == "wrapped [Wrapped]");
        Assert.That(wrapped.Value, Is.EqualTo("Wrapped: 3"), "The DebuggerDisplay expression is evaluated in the debuggee");

        var members = GetVariables(wrapped.VariablesReference);
        Assert.That(members.Select(it => it.Name), Does.Contain("Value [int]"));
        Assert.That(members.Any(it => it.Name.StartsWith("Hidden")), Is.False, "DebuggerBrowsable(Never) hides the member");
    }

    // The type's initializer has not run at the stop: the listing shows the field's current (default)
    // value rather than running the initializer, which would change the program's behavior
    [Test]
    public void StaticFieldOfUninitializedTypeTest() {
        var threadId = LaunchToMarker();
        var holder = GetLocalVariables(threadId).First(it => it.Name == "holder [Holder]");

        var staticGroup = GetVariables(holder.VariablesReference).First(it => it.Name == "Static members");
        var counter = GetVariables(staticGroup.VariablesReference).First(it => it.Name == "Counter [int]");
        Assert.That(counter.Value, Is.EqualTo("0"));
    }

    // A constant has no storage, the metadata literal is shown; writing it is refused rather than misreported as done
    [Test]
    public void ConstantFieldCannotBeAssignedTest() {
        var threadId = LaunchToMarker();
        var holder = GetLocalVariables(threadId).First(it => it.Name == "holder [Holder]");
        var staticGroup = GetVariables(holder.VariablesReference).First(it => it.Name == "Static members");
        Assert.That(GetVariables(staticGroup.VariablesReference).First(it => it.Name == "Limit [int]").Value, Is.EqualTo("10"));

        var error = Assert.Throws<ProtocolException>(() => Host.SendRequestSync(new SetVariableRequest() {
            VariablesReference = staticGroup.VariablesReference,
            Name = "Limit [int]",
            Value = "11",
        }));
        Assert.That(error!.Message, Does.Contain("constant"));
    }

    // A nullable's display comes from its underlying value: the ToString of a Guid, the fields of a struct
    [Test]
    public void NullableStructDisplayTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        Assert.That(locals.First(it => it.Name == "identifier [Guid?]").Value, Is.EqualTo("{00000000-0000-0000-0000-000000000000}"), "The underlying value's ToString is shown, not the template");
        var point = locals.First(it => it.Name == "point [Point?]");
        var members = GetVariables(point.VariablesReference).Select(it => it.Name).ToList();
        Assert.That(members, Does.Contain("X [int]").And.Contain("Y [int]"), "A nullable struct expands to the struct's own members");
        Assert.That(members, Does.Not.Contain("HasValue [bool]"));
    }

    [Test]
    public void JaggedArrayFormatTest() {
        var threadId = LaunchToMarker();
        var jagged = GetLocalVariables(threadId).First(it => it.Name == "jagged [int[][]]");
        Assert.That(jagged.Value, Is.EqualTo("{int[3][]}"), "The length goes into the array's own brackets");
    }

    // DebuggerDisplay and DebuggerTypeProxy are inherited: a List<int> subclass shows List<int>'s count and items
    [Test]
    public void InheritedDisplayAttributesTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        Assert.That(locals.First(it => it.Name == "tagged [Tagged]").Value, Is.EqualTo("Tagged 5"), "The base class's DebuggerDisplay applies");
        var numbers = locals.First(it => it.Name == "numbers [NumberList]");
        Assert.That(numbers.Value, Is.EqualTo("Count = 3"));
        var members = GetVariables(numbers.VariablesReference).Select(it => it.Name).ToList();
        Assert.That(members, Does.Contain("[0] [int]"), "The base class's proxy lists the items");
    }
}
