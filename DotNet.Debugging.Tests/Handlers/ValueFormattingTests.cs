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
        Console.WriteLine($"{color}{access}{unnamed}{maybe}{nothing}{price}{letter}{boxed}{anonymous}{wrapped}{holder}"); // marker:stop

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
        }
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

    [Test]
    public void AnonymousTypeDisplayTest() {
        var threadId = LaunchToMarker();
        var anonymous = GetLocalVariables(threadId).First(it => it.Name.StartsWith("anonymous"));
        Assert.That(anonymous.Value, Is.EqualTo("{ Id = 7, Name = seven }"), "The anonymous type's DebuggerDisplay runs with its escaped braces");
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
}
