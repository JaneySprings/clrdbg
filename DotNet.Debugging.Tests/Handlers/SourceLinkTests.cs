using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNet.Debugging.Adapter;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The debuggee is compiled with its source path mapped to a location that does not exist ('/_/src'), the way
// deterministic builds of packages do, and a Source Link map pointing at a local HTTP server serving the sources.
// The SDK's own Source Link generation is disabled, it would map the sandbox to this repository's GitHub URL
public class SourceLinkTests : BaseDebugTestFixture {
    private const string MappedSourceRoot = "/_/src";

    private readonly int port;
    private HttpListener? listener;
    private int servedRequests;

    public SourceLinkTests() : base(nameof(SourceLinkTests)) {
        port = GetFreePort();
    }

    protected override string CreateProgramFileContent() {
        return """
        var greeting = "hello";
        Console.WriteLine(greeting); // marker:stop
        Console.WriteLine("done"); // marker:end
        """;
    }
    protected override string CreateProjectFileContent() {
        return $"""
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RollForward>major</RollForward>
                <DebugType>portable</DebugType>
                <EnableSourceLink>false</EnableSourceLink>
                <PathMap>$(MSBuildProjectDirectory)={MappedSourceRoot}</PathMap>
                <SourceLink>$(MSBuildProjectDirectory)/sourcelink.json</SourceLink>
            </PropertyGroup>
        </Project>
        """;
    }
    protected override void CreateProjectFiles(string projectDirectory) {
        var json = $$$"""{"documents": {"{{{MappedSourceRoot}}}/*": "http://127.0.0.1:{{{port}}}/*"}}""";
        File.WriteAllText(Path.Combine(projectDirectory, "sourcelink.json"), json);
    }

    [OneTimeSetUp]
    public void StartSourceServer() {
        listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        listener.BeginGetContext(OnRequest, null);
    }
    [OneTimeTearDown]
    public void StopSourceServer() {
        listener?.Stop();
        listener?.Close();
    }

    [Test]
    public void MissingSourceIsServedOnRequestTest() {
        var sourceLinkOptions = JObject.Parse("""{"*": {"enabled": true}}""");
        var threadId = LaunchToMarker(properties: new Dictionary<string, JToken> { ["sourceLinkOptions"] = sourceLinkOptions });

        // The mapped document does not exist, the frame refers to a source the client has to ask for
        var frame = GetTopStackFrame(threadId);
        Assert.That(frame.Source?.Path?.Replace('\\', '/'), Is.EqualTo($"{MappedSourceRoot}/Program.cs"));
        Assert.That(frame.Source!.SourceReference, Is.GreaterThan(0));
        Assert.That(frame.Source.VsSourceLinkInfo?.Url, Is.EqualTo($"http://127.0.0.1:{port}/Program.cs"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:stop")));
        Assert.That(Volatile.Read(ref servedRequests), Is.Zero, "Nothing is downloaded until the client opens the document");

        var source = Host.SendRequestSync(new SourceRequest() { Source = frame.Source, SourceReference = frame.Source.SourceReference!.Value });
        Assert.That(source.Content, Is.EqualTo(CreateProgramFileContent()));
        Host.SendRequestSync(new SourceRequest() { Source = frame.Source, SourceReference = frame.Source.SourceReference!.Value });
        Assert.That(Volatile.Read(ref servedRequests), Is.EqualTo(1), "The downloaded source is cached");

        // Breakpoints set in the served document bind through its document path and keep its reference
        var breakpoints = Host.SendRequestSync(new SetBreakpointsRequest() {
            Source = new Source() { Name = frame.Source.Name, Path = frame.Source.Path, SourceReference = frame.Source.SourceReference },
            Breakpoints = new List<SourceBreakpoint> { new SourceBreakpoint() { Line = GetMarkerLine("marker:end") } },
        }).Breakpoints;
        Assert.That(breakpoints[0].Verified, Is.True, breakpoints[0].Message);
        Assert.That(breakpoints[0].Source?.SourceReference, Is.EqualTo(frame.Source.SourceReference));

        Continue(threadId);
        var endStopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var endFrame = GetTopStackFrame(endStopped.ThreadId!.Value);
        Assert.That(endFrame.Line, Is.EqualTo(GetMarkerLine("marker:end")));
        Assert.That(endFrame.Source?.SourceReference, Is.EqualTo(frame.Source.SourceReference), "The same document keeps its reference across stops");
    }

    [Test]
    public void DisabledSourceLinkKeepsThePlainPathTest() {
        var sourceLinkOptions = JObject.Parse("""{"*": {"enabled": false}}""");
        var threadId = LaunchToMarker(properties: new Dictionary<string, JToken> { ["sourceLinkOptions"] = sourceLinkOptions });

        var frame = GetTopStackFrame(threadId);
        Assert.That(frame.Source?.Path?.Replace('\\', '/'), Is.EqualTo($"{MappedSourceRoot}/Program.cs"));
        Assert.That(frame.Source?.SourceReference ?? 0, Is.Zero);
        Assert.That(Volatile.Read(ref servedRequests), Is.Zero);
    }

    private void OnRequest(IAsyncResult result) {
        HttpListenerContext context;
        try {
            context = listener!.EndGetContext(result);
        }
        catch (Exception) {
            return; // the listener was stopped
        }
        listener.BeginGetContext(OnRequest, null);

        Interlocked.Increment(ref servedRequests);
        var filePath = Path.Combine(ProjectDirectory, context.Request.Url!.AbsolutePath.TrimStart('/'));
        var content = File.Exists(filePath) ? File.ReadAllBytes(filePath) : Encoding.UTF8.GetBytes("not found");
        context.Response.StatusCode = File.Exists(filePath) ? 200 : 404;
        context.Response.ContentLength64 = content.Length;
        context.Response.OutputStream.Write(content);
        context.Response.Close();
    }
    private static int GetFreePort() {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
}
