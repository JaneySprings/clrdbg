using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Soft;

public interface IDebugAgent : IDisposable {
    public BaseConfiguration Configuration { get; }

    public void PrepareTarget(DebugSession debugSession);
    public void Connect(ManagedDebugger debugger);
}

public abstract class BaseDebugAgent<TConfiguration> : IDebugAgent where TConfiguration : BaseConfiguration {
    protected List<Action> Disposables { get; }
    protected CurrentClassLogger Logger { get; }

    public TConfiguration Configuration { get; }

    protected BaseDebugAgent(TConfiguration configuration) {
        Logger = new CurrentClassLogger(nameof(IDebugAgent));
        Disposables = new List<Action>();
        Configuration = configuration;
    }

    public abstract void PrepareTarget(DebugSession debugSession);
    public abstract void Connect(ManagedDebugger debugger);

    BaseConfiguration IDebugAgent.Configuration => Configuration;

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