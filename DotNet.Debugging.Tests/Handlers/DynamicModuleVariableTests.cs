using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Types emitted at run time (Reflection.Emit, and with it every mocking library - Moq, NSubstitute, FakeItEasy)
// live in a dynamic module, which has no metadata the debugger can read and no base address to identify it by
public class DynamicModuleVariableTests : BaseDebugTestFixture {
    public DynamicModuleVariableTests() : base(nameof(DynamicModuleVariableTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Reflection;
        using System.Reflection.Emit;

        var emitted = Emitter.Create("emitted proxy");
        var derived = Emitter.CreateDerived();
        var plain = Emitter.Create(null);
        Console.WriteLine($"{emitted} {derived} {plain}"); // marker:stop
        Console.WriteLine("done");

        // Builds a type in a dynamic module of its own the way a mocking library builds a proxy: a field emitted
        // into the module, and a 'ToString' override emitted there too when a result for it is given
        public static class Emitter {
            public static object Create(string? toStringResult) => Create(typeof(object), toStringResult);
            public static object CreateDerived() => Create(typeof(ModelPart), null);

            private static object Create(Type baseType, string? toStringResult) {
                var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"Emitted{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
                var module = assembly.DefineDynamicModule("EmittedModule");
                var type = module.DefineType("EmittedProxy", TypeAttributes.Public, baseType);
                type.DefineField("Tag", typeof(int), FieldAttributes.Public);
                if (toStringResult != null) {
                    var method = type.DefineMethod("ToString",
                        MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                        typeof(string), Type.EmptyTypes);
                    var il = method.GetILGenerator();
                    il.Emit(OpCodes.Ldstr, toStringResult);
                    il.Emit(OpCodes.Ret);
                    type.DefineMethodOverride(method, typeof(object).GetMethod("ToString")!);
                }
                var created = type.CreateType();
                var instance = Activator.CreateInstance(created)!;
                created.GetField("Tag")!.SetValue(instance, 7);
                return instance;
            }
        }

        public class ModelPart {
            public int Id = 42;
            public override string ToString() => $"ModelPart({Id})";
        }
        """;
    }

    [Test]
    public void EmittedToStringOverrideTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        var emitted = locals.FirstOrDefault(it => it.Name.StartsWith("emitted "));
        Assert.That(emitted, Is.Not.Null);
        Assert.That(emitted!.Value, Is.EqualTo("{emitted proxy}"));
    }

    [Test]
    public void EmittedTypeWithBaseToStringTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        // The override lives on the base type, in a module with metadata, and is reached through the emitted type
        var derived = locals.FirstOrDefault(it => it.Name.StartsWith("derived "));
        Assert.That(derived, Is.Not.Null);
        Assert.That(derived!.Value, Is.EqualTo("{ModelPart(42)}"));
    }

    [Test]
    public void EmittedTypeWithoutToStringTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        var plain = locals.FirstOrDefault(it => it.Name.StartsWith("plain "));
        Assert.That(plain, Is.Not.Null);
        Assert.That(plain!.Value, Is.EqualTo("{EmittedProxy}"));
    }

    [Test]
    public void EmittedTypeMembersTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        var derived = locals.FirstOrDefault(it => it.Name.StartsWith("derived "));
        Assert.That(derived, Is.Not.Null);
        var members = GetVariables(derived!.VariablesReference);
        var id = members.FirstOrDefault(it => it.Name.StartsWith("Id "));
        Assert.That(id, Is.Not.Null);
        Assert.That(id!.Value, Is.EqualTo("42"));
    }

    // A field declared by the emitted type itself, whose metadata only the runtime holds
    [Test]
    public void EmittedTypeOwnMembersTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        var emitted = locals.FirstOrDefault(it => it.Name.StartsWith("emitted "));
        Assert.That(emitted, Is.Not.Null);
        var members = GetVariables(emitted!.VariablesReference);
        var tag = members.FirstOrDefault(it => it.Name.StartsWith("Tag "));
        Assert.That(tag, Is.Not.Null);
        Assert.That(tag!.Value, Is.EqualTo("7"));
    }

    [Test]
    public void EmittedValueEvaluateTest() {
        var threadId = LaunchToMarker();

        Assert.That(Evaluate("emitted", threadId).Result, Is.EqualTo("{emitted proxy}"));
        Assert.That(Evaluate("((ModelPart)derived).Id", threadId).Result, Is.EqualTo("42"));
    }

    // The type is defined during the evaluation itself, the way a mock's proxy is when its 'Object' is first
    // read in a watch: the module and its type only become known to the debugger while the evaluation runs
    [Test]
    public void EmittedTypeCreatedDuringEvaluationTest() {
        var threadId = LaunchToMarker();

        var result = Evaluate("Emitter.Create(\"late proxy\")", threadId);
        Assert.That(result.Result, Is.EqualTo("{late proxy}"));
        var members = GetVariables(result.VariablesReference);
        Assert.That(members.FirstOrDefault(it => it.Name.StartsWith("Tag "))?.Value, Is.EqualTo("7"));
    }
}
