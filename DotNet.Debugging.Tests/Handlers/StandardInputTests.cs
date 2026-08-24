using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class StandardInputTests : BaseDebugTestFixture {
    public StandardInputTests() : base(nameof(StandardInputTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        Console.Write("Enter name: ");
        var line = Console.ReadLine();
        Console.WriteLine($"echo:{line}");
        while (true) {
            Thread.Sleep(50);
        }
        """;
    }

    [Test]
    public void ReplInputWhileRunningIsWrittenToStandardInputTest() {
        Launch();
        ConfigurationDone();
        // The unterminated prompt must arrive while the debuggee is blocked reading the answer
        WaitForEvent<OutputEvent>(it => it.Output.Contains("Enter name: ", StringComparison.Ordinal));

        // Typing in the Debug Console asks for completions on every keystroke, which must not fault the session
        var completions = Host.SendRequestSync(new CompletionsRequest() { Text = "h", Column = 2, Line = 1 });
        Assert.That(completions.Targets, Is.Empty);

        var response = Host.SendRequestSync(new EvaluateRequest() {
            Expression = "hello stdin",
            Context = EvaluateArguments.ContextValue.Repl,
        });

        // vsdbg answers console input with an empty result marked 'failedEvaluation', so nothing is printed for it
        Assert.That(response.Result, Is.Empty);
        Assert.That(response.PresentationHint?.Attributes, Is.Not.Null);
        Assert.That(response.PresentationHint!.Attributes!.Value.HasFlag(VariablePresentationHint.AttributesValue.FailedEvaluation), Is.True);
        WaitForEvent<OutputEvent>(it => it.Output.Contains("echo:hello stdin", StringComparison.Ordinal));
    }
}
