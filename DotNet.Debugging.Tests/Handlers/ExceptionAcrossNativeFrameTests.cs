using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// An exception that leaves user code into a native frame of the runtime is raised again in the managed caller beyond
// it, with the user frames unwound already: here a callback the native library loader calls throws, and the raise
// comes back in the core library's load method. It is the same exception, still the user module's. A different
// exception the runtime raises there in its place (a failed type initializer's wrapper) is the core library's own
public class ExceptionAcrossNativeFrameTests : BaseDebugTestFixture {
    public ExceptionAcrossNativeFrameTests() : base(nameof(ExceptionAcrossNativeFrameTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Diagnostics;
        using System.Reflection;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Runtime.Loader;

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += Callbacks.OnResolvingUnmanagedDll;
        Framework.Load();
        try {
            NativeLibrary.Load("a-native-library-that-does-not-exist", typeof(Callbacks).Assembly, null);
        } catch (InvalidOperationException ex) {
            Console.WriteLine($"caught: {ex.Message}");
        }
        try {
            RuntimeHelpers.RunClassConstructor(typeof(Faulty).TypeHandle);
        } catch (TypeInitializationException ex) {
            Console.WriteLine($"caught: {ex.InnerException?.Message}");
        }
        Console.WriteLine("done");

        public static class Callbacks {
            public static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string name) {
                throw new InvalidOperationException("resolve boom"); // marker:throw
            }
        }
        // Stands for a framework calling into the app: not user code, and it swallows what the app throws
        public static class Framework {
            [DebuggerNonUserCode]
            public static void Load() {
                try {
                    NativeLibrary.Load("a-native-library-that-does-not-exist", typeof(Callbacks).Assembly, null);
                } catch (InvalidOperationException) {
                    Console.WriteLine("swallowed");
                }
            }
        }
        public static class Faulty {
            static Faulty() {
                throw new InvalidOperationException("initializer boom");
            }
        }
        """;
    }

    [Test]
    public void ExceptionRaisedAgainBeyondANativeFrameIsUserUnhandledInTheUserModuleTest() {
        LaunchWithExceptionFilters("user-unhandled");

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo($"An exception of type 'System.InvalidOperationException' occurred in {ProjectName}.dll but was not handled in user code"),
            "The raise repeated in the core library continues the one made in user code");

        var info = GetExceptionInfo(stopped.ThreadId!.Value);
        Assert.That(info.BreakMode, Is.EqualTo(ExceptionBreakMode.UserUnhandled));
        Assert.That(info.Details?.Message, Is.EqualTo("resolve boom"));
        // The throwing frame is unwound by now, the recorded trace still begins at it
        Assert.That(info.Details?.StackTrace, Does.StartWith("   at Callbacks.OnResolvingUnmanagedDll("));
        Assert.That(info.Details?.StackTrace, Does.Contain($":line {GetMarkerLine("marker:throw")}"));

        Continue(stopped.ThreadId!.Value);
        Assert.That(CollectStopsUntilExit(), Is.Empty, "What user code catches itself stops nothing");
    }

    [Test]
    public void EachStopNamesTheModuleOfItsRaiseTest() {
        LaunchWithExceptionFilters("all");

        var texts = new List<string>();
        CollectStopsUntilExit(it => texts.Add(it.Text));

        var userRaise = $"Exception thrown: 'System.InvalidOperationException' in {ProjectName}.dll";
        Assert.That(texts, Is.EqualTo(new[] {
            userRaise, // thrown by the callback under Framework.Load, swallowed there
            userRaise, // thrown by the callback under Main ...
            userRaise, // ... and raised again beyond the native frame, the dispatch entering Main
            userRaise, // thrown by the type initializer
            "Exception thrown: 'System.TypeInitializationException' in System.Private.CoreLib.dll",
        }));
    }
}
