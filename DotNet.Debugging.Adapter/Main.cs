using DotNet.Debugging.Adapter.Terminal;

namespace DotNet.Debugging.Adapter;

public class Program {
    public const string ConnectionOption = "--connection=";

    private static int Main(string[] args) {
#if DEBUG
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAIT_FOR_DEBUGGER"))) {
            while (!System.Diagnostics.Debugger.IsAttached)
                Thread.Sleep(500);
        }
#endif

        foreach (var arg in args) {
            if (arg.StartsWith(ConnectionOption, StringComparison.OrdinalIgnoreCase))
                return TerminalHost.Run(arg.Substring(ConnectionOption.Length));
        }

        var debugSession = new DebugSession(Console.OpenStandardInput(), Console.OpenStandardOutput());
        debugSession.Start();
        return 0;
    }
}
