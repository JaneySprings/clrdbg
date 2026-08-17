namespace DotNet.Debugging.Adapter;

public class Program {
    private static void Main(string[] args) {
#if DEBUG
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAIT_FOR_DEBUGGER"))) {
            while (!System.Diagnostics.Debugger.IsAttached)
                Thread.Sleep(500);
        }
#endif

        var debugSession = new DebugSession(Console.OpenStandardInput(), Console.OpenStandardOutput());
        debugSession.Start();
    }
}