using DotNet.Debugging.Adapter;
using DotNet.Debugging.Adapter.Terminal;
using DotNet.Debugging.Engine;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A launched debuggee is parked at startup by DOTNET_DefaultDiagnosticPortSuspend, which the debugger puts into the
// environment it builds for it. An environment is inherited, so a child process of the debuggee would be handed the
// same variable: a managed child would then park on its own diagnostics port, where no debugger is listening, and
// never exit - which hangs the debuggee itself the moment it waits for it. The debugger takes the variable back out
// of the debuggee's environment before letting it run, or puts back the value the launch configuration gave it
public class ChildProcessTests : BaseDebugTestFixture {
    public ChildProcessTests() : base(nameof(ChildProcessTests)) { }

    // The result travels through a file rather than the debuggee's output: a debuggee launched in a terminal
    // prints to the terminal, and only a launched one has its streams redirected into the adapter
    private string ResultPath => Path.Combine(Path.GetDirectoryName(ProgramPath)!, "child.result");

    protected override string CreateProgramFileContent() {
        return """
        using System.Diagnostics;
        using System.Reflection;

        // The debuggee starts itself again: what reproduces the hang is a child process with a runtime of its own
        if (args.Length > 0 && args[0] == "child") {
            var suspend = Environment.GetEnvironmentVariable("DOTNET_DefaultDiagnosticPortSuspend");
            Console.WriteLine($"child ran, suspend={suspend ?? "none"}");
            return;
        }

        var startInfo = new ProcessStartInfo(Environment.ProcessPath!) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        startInfo.ArgumentList.Add("child");

        var child = Process.Start(startInfo)!;
        var childOutput = child.StandardOutput.ReadToEndAsync();
        _ = child.StandardError.ReadToEndAsync();
        var exited = child.WaitForExit(20000);
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "child.result"),
            exited ? $"exited: {childOutput.Result.Trim()}" : "the child never exited");
        if (!exited)
            child.Kill(entireProcessTree: true);
        Console.WriteLine("done"); // marker:end
        """;
    }

    [SetUp]
    public void DeleteResult() {
        File.Delete(ResultPath);
    }

    [Test]
    public void ChildProcessOfALaunchedDebuggeeRunsTest() {
        Launch();
        ConfigurationDone();

        Assert.That(WaitForResult(), Is.EqualTo("exited: child ran, suspend=none"));
        WaitForEvent<TerminatedEvent>();
    }

    // Both terminal kinds are started by the same terminal host, the test stands in for the client's terminal either way
    [TestCase("integratedTerminal")]
    [TestCase("externalTerminal")]
    public void ChildProcessOfATerminalDebuggeeRunsTest(string console) {
        Host.RequestReceived += (_, args) => {
            if (args.Args is not RunInTerminalArguments runInTerminal)
                return;
            var connection = runInTerminal.Args.FirstOrDefault(it => it.StartsWith(Program.ConnectionOption, StringComparison.Ordinal));
            Assert.That(connection, Is.Not.Null, "The runInTerminal request names no '--connection' pipe");
            Task.Run(() => TerminalHost.Run(connection!.Substring(Program.ConnectionOption.Length)));
            args.Response = new RunInTerminalResponse();
        };

        Launch(properties: new Dictionary<string, JToken> { ["console"] = console });
        ConfigurationDone();

        Assert.That(WaitForResult(), Is.EqualTo("exited: child ran, suspend=none"));
    }

    // A value the launch configuration itself sets is the user's: it is handed on to the child rather than removed
    [Test]
    public void ConfiguredSuspendVariableReachesTheChildTest() {
        var environment = new JObject();
        environment[ManagedDebugger.DiagnosticPortSuspendVariable] = "0";
        Launch(properties: new Dictionary<string, JToken> { ["env"] = environment });
        ConfigurationDone();

        Assert.That(WaitForResult(), Is.EqualTo("exited: child ran, suspend=0"));
        WaitForEvent<TerminatedEvent>();
    }

    // The debuggee gives up on its child after 20 seconds and reports that instead, so the wait outlasts it:
    // a failure then names what went wrong rather than timing out
    private string WaitForResult(int timeoutMs = 40000) {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline) {
            if (File.Exists(ResultPath))
                return File.ReadAllText(ResultPath).Trim();
            System.Threading.Thread.Sleep(100);
        }
        throw new TimeoutException("The debuggee reported nothing about its child process");
    }
}
