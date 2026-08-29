using DotNet.Debugging.Adapter.Terminal;

namespace DotNet.Debugging.Adapter;

public class Program {
    public const string ConnectionOption = "--connection=";
    public const string PauseForDebuggerOption = "--pauseAdapterForDebugger";

    private static int Main(string[] args) {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        foreach (var arg in args) {
            if (arg.StartsWith(ConnectionOption, StringComparison.OrdinalIgnoreCase))
                return TerminalHost.Run(arg.Substring(ConnectionOption.Length));
            if (arg.StartsWith(PauseForDebuggerOption, StringComparison.OrdinalIgnoreCase)) {
                while (!System.Diagnostics.Debugger.IsAttached)
                    Thread.Sleep(500);
            }
        }

        var debugSession = new DebugSession(Console.OpenStandardInput(), Console.OpenStandardOutput());
        debugSession.Start();
        return 0;
    }
}
