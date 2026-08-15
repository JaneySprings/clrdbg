using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Soft;

public interface IDebugAgent : IDisposable {
    public BaseConfiguration Configuration { get; }
    public void Connect(ManagedDebugger debugger);
}

public abstract class BaseDebugAgent<TConfiguration> : IDebugAgent where TConfiguration : BaseConfiguration {
    protected CurrentClassLogger Logger { get; }
    protected List<Action> Disposables { get; }
    protected DebugSession DebugSession { get; }

    public TConfiguration Configuration { get; }
    BaseConfiguration IDebugAgent.Configuration => Configuration;

    protected BaseDebugAgent(TConfiguration configuration, DebugSession debugSession) {
        Logger = new CurrentClassLogger(nameof(IDebugAgent));
        Disposables = new List<Action>();
        Configuration = configuration;
        DebugSession = debugSession;
    }

    public abstract void Connect(ManagedDebugger debugger);
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