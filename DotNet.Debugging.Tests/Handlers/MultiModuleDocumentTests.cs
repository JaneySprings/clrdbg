using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The program and a library it references both compile 'Shared/Shared.cs' (linked into the library) and a byte-identical
// 'Common.cs' each from their own folder: a breakpoint in such a file is hit in every module that compiled it
public class MultiModuleDocumentTests : BaseDebugTestFixture {
    private const string CommonFileContent = """
        internal static class Common {
            public static void Touch(string origin) {
                Console.WriteLine($"Common from {origin}"); // marker:touch
            }
        }
        """;

    public MultiModuleDocumentTests() : base(nameof(MultiModuleDocumentTests)) { }

    // The library builds into its own bin/obj inside the sandbox, which the reuse fingerprint cannot ignore
    protected override bool ReuseSandbox => false;

    protected override string CreateProgramFileContent() {
        return """
        SampleLibrary.Worker.Run();
        Common.Touch("program");
        Shared.Touch("program");
        Console.WriteLine("done");
        """;
    }
    protected override string CreateProjectFileContent() {
        return """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RollForward>major</RollForward>
            </PropertyGroup>
            <ItemGroup>
                <Compile Remove="Lib\**" />
                <ProjectReference Include="Lib\Lib.csproj" />
            </ItemGroup>
        </Project>
        """;
    }
    protected override void CreateProjectFiles(string projectDirectory) {
        File.WriteAllText(Path.Combine(projectDirectory, "Common.cs"), CommonFileContent);
        var sharedDirectory = Path.Combine(projectDirectory, "Shared");
        Directory.CreateDirectory(sharedDirectory);
        File.WriteAllText(Path.Combine(sharedDirectory, "Shared.cs"), """
        internal static class Shared {
            public static void Touch(string origin) {
                Console.WriteLine($"Shared from {origin}"); // marker:shared
            }
        }
        """);

        var libraryDirectory = Path.Combine(projectDirectory, "Lib");
        Directory.CreateDirectory(libraryDirectory);
        File.WriteAllText(Path.Combine(libraryDirectory, "Common.cs"), CommonFileContent);
        File.WriteAllText(Path.Combine(libraryDirectory, "Lib.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
            </PropertyGroup>
            <ItemGroup>
                <Compile Include="..\Shared\Shared.cs" Link="Shared.cs" />
            </ItemGroup>
        </Project>
        """);
        File.WriteAllText(Path.Combine(libraryDirectory, "Worker.cs"), """
        namespace SampleLibrary;
        public static class Worker {
            public static void Run() {
                Common.Touch("library");
                Shared.Touch("library");
            }
        }
        """);
    }

    [Test]
    public void IdenticalCopyIsHitInBothModulesTest() {
        var libraryCopyPath = Path.Combine(ProjectDirectory, "Lib", "Common.cs");
        var programCopyPath = Path.Combine(ProjectDirectory, "Common.cs");
        var line = FindMarkerLine(libraryCopyPath, "marker:touch");

        Launch();
        SetBreakpoint(libraryCopyPath, line);
        ConfigurationDone();

        // The program's copy binds by its content; the breakpoint reports the library's document, the one it was set in
        WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Verified && it.Breakpoint.Source?.Path == libraryCopyPath);
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Source?.Path, Is.EqualTo(libraryCopyPath));
        Assert.That(frame.Line, Is.EqualTo(line));

        Continue(stopped.ThreadId!.Value);
        stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Source?.Path, Is.EqualTo(programCopyPath));
    }

    [Test]
    public void LinkedFileIsHitInEveryModuleTest() {
        var sharedPath = Path.Combine(ProjectDirectory, "Shared", "Shared.cs");
        var line = FindMarkerLine(sharedPath, "marker:shared");

        Launch();
        SetBreakpoint(sharedPath, line);
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Name, Does.StartWith("Lib.dll!"));
        Assert.That(frame.Source?.Path, Is.EqualTo(sharedPath));
        Assert.That(frame.Line, Is.EqualTo(line));

        Continue(stopped.ThreadId!.Value);
        stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Name, Does.StartWith($"{nameof(MultiModuleDocumentTests)}.dll!"));
        Assert.That(frame.Source?.Path, Is.EqualTo(sharedPath));
    }

    private void SetBreakpoint(string filePath, int line) {
        Host.SendRequestSync(new SetBreakpointsRequest() {
            Source = new Source() { Path = filePath },
            Breakpoints = new List<SourceBreakpoint> { new SourceBreakpoint() { Line = line } },
        });
    }
    private static int FindMarkerLine(string filePath, string marker) {
        var lines = File.ReadAllLines(filePath);
        for (var index = 0; index < lines.Length; index++) {
            if (lines[index].Contains(marker))
                return index + 1;
        }
        throw new InvalidOperationException($"Marker '{marker}' not found in '{filePath}'");
    }
}
