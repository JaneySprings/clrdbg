using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Moq builds its proxies with Castle DynamicProxy, into the dynamic module 'DynamicProxyGenAssembly2'.
// A local check against the real package (it needs a NuGet restore), run by selecting it explicitly:
// dotnet test --filter "FullyQualifiedName~MoqVariableTests"
[Explicit("Local check against the Moq package, needs a NuGet restore")]
public class MoqVariableTests : BaseDebugTestFixture {
    public MoqVariableTests() : base(nameof(MoqVariableTests)) { }

    protected override string CreateProjectFileContent() {
        return """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RollForward>major</RollForward>
                <NoWarn>$(NoWarn);CS0414;CS0169;CS0219</NoWarn>
            </PropertyGroup>
            <ItemGroup>
                <PackageReference Include="Moq" Version="4.20.72" />
            </ItemGroup>
        </Project>
        """;
    }
    protected override string CreateProgramFileContent() {
        return """
        using Moq;

        var mock = new Mock<IGreeter>();
        mock.Setup(it => it.Greet("world")).Returns("hello world");
        mock.SetupGet(it => it.Count).Returns(3);
        var greeter = mock.Object;
        var greeting = greeter.Greet("world");
        var service = new Service(greeter);
        var lazy = new Mock<IRepository>();
        Console.WriteLine($"{greeting} {service} {lazy}"); // marker:stop
        Console.WriteLine("done");

        public interface IGreeter {
            int Count { get; }
            string Greet(string name);
        }
        public interface IRepository {
            void Save(string item);
        }
        public class Service {
            public IGreeter Greeter;
            public Service(IGreeter greeter) => Greeter = greeter;
        }
        """;
    }

    [Test]
    public void MockVariablesTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);
        Dump("locals", locals);

        foreach (var name in new[] { "mock", "greeter", "service", "lazy" }) {
            var variable = locals.First(it => it.Name.StartsWith(name + " "));
            Assert.That(variable.Value, Does.Not.StartWith("error"), name);
            Assert.That(variable.VariablesReference, Is.GreaterThan(0), name);
            var members = GetVariables(variable.VariablesReference);
            Dump(name, members);
            foreach (var member in members) {
                Assert.That(member.Value, Does.Not.StartWith("error"), $"{name}.{member.Name}");
                // One level deeper: the proxy's own state, the service's proxy field
                if (member.VariablesReference > 0 && (name == "greeter" || name == "service"))
                    Dump($"{name}.{member.Name}", GetVariables(member.VariablesReference));
            }
        }
    }

    // The proxy of 'lazy' does not exist at the stop: Castle emits its type while the evaluation runs
    [Test]
    public void LazyProxyEvaluateTest() {
        var threadId = LaunchToMarker();

        var lazyObject = Evaluate("lazy.Object", threadId);
        TestContext.Out.WriteLine($"lazy.Object = {lazyObject.Result} [{lazyObject.Type}]");
        Assert.That(lazyObject.Result, Does.Not.StartWith("error"));
        var members = GetVariables(lazyObject.VariablesReference);
        Dump("lazy.Object", members);
        Assert.That(members.All(it => !it.Value.StartsWith("error")));

        Assert.That(Evaluate("greeter.Count", threadId).Result, Is.EqualTo("3"));
        Assert.That(Evaluate("greeter.Greet(\"world\")", threadId).Result, Is.EqualTo("\"hello world\""));
        var mockObject = Evaluate("mock.Object", threadId);
        TestContext.Out.WriteLine($"mock.Object = {mockObject.Result} [{mockObject.Type}]");
        Assert.That(mockObject.Result, Does.Not.StartWith("error"));
    }

    private static void Dump(string title, IEnumerable<Variable> variables) {
        TestContext.Out.WriteLine($"--- {title} ---");
        foreach (var variable in variables)
            TestContext.Out.WriteLine($"  {variable.Name} = {variable.Value}  [{variable.Type}] ref={variable.VariablesReference}");
    }
}
