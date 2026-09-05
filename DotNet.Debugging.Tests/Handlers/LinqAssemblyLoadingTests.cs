using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A program that never uses System.Linq does not load it, and an expression compiles against the loaded modules
// only: one that needs the assembly gets it loaded into the debuggee and is compiled again
public class LinqAssemblyLoadingTests : BaseDebugTestFixture {
    public LinqAssemblyLoadingTests() : base(nameof(LinqAssemblyLoadingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var strings = new List<string> { "aa", "ab", "bb" };
        Console.WriteLine(strings.Count); // marker:stop
        Console.WriteLine("done");
        """;
    }

    [Test]
    public void LinqOperatorLoadsSystemLinqTest() {
        var threadId = LaunchToMarker();
        Assert.That(ReceivedEvents.OfType<ModuleEvent>().Any(it => it.Module.Name == "System.Linq.dll"), Is.False, "The program never loads System.Linq");

        Assert.That(Evaluate("strings.Where(x => x.Contains(\"a\")).Count()", threadId).Result, Is.EqualTo("2"));
        // The evaluation loaded System.Linq into the debuggee; its module event may trail the response
        WaitForEvent<ModuleEvent>(it => it.Module.Name == "System.Linq.dll");

        // Compiled against the assembly now loaded, no second load
        Assert.That(Evaluate("strings.Where(x => x.Contains(\"a\"))", threadId).Result, Is.EqualTo("{string[2]}"));
        Assert.That(Evaluate("strings.Select(x => x.Length).Sum()", threadId).Result, Is.EqualTo("6"));
        Assert.That(ReceivedEvents.OfType<ModuleEvent>().Count(it => it.Module.Name == "System.Linq.dll"), Is.EqualTo(1));
    }

    // An error the loading cannot fix keeps the compiler's message
    [Test]
    public void UnknownMemberIsStillReportedTest() {
        var threadId = LaunchToMarker();
        var error = Assert.Throws<Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.ProtocolException>(() => Evaluate("strings.Missing(x => x)", threadId));
        Assert.That(error!.Message, Does.Contain("does not contain a definition for 'Missing'"));
    }
}
