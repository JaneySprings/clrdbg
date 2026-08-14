using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Soft;

public abstract class BaseLaunchAgent {
    protected List<Action> Disposables { get; init; }
    protected LaunchConfiguration Configuration { get; init; }
    protected CurrentClassLogger Logger { get; init; }

    protected BaseLaunchAgent(LaunchConfiguration configuration) {
        Logger = new CurrentClassLogger(nameof(BaseLaunchAgent));
        Disposables = new List<Action>();
        Configuration = configuration;
    }

    public abstract void Connect(ManagedDebugger debugger);
    public abstract void Launch(DebugSession debugSession);


    public void Dispose() {
        foreach (var disposable in Disposables) {
            try {
                disposable.Invoke();
                Logger.Debug($"Disposing {disposable.Method.Name}");
            }
            catch (Exception ex) {
                Logger.Error($"Error while disposing {disposable.Method.Name}: {ex}");
            }
        }
        Disposables.Clear();
    }
}