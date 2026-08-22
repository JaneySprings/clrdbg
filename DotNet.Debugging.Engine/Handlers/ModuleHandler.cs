using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Evaluation;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Metadata;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    private void HandleModuleLoaded(LoadModuleCorDebugManagedCallbackEventArgs callbackEvent) {
        var corModule = callbackEvent.Module;
        var modulePath = corModule.GetName();
        var moduleName = Path.GetFileName(modulePath);
        DebuggerLoggingService.LogMessage($"Module loaded: {modulePath} at 0x{corModule.GetBaseAddress().Value:X}");

        var metadataReader = LoadModuleMetadata(corModule, modulePath);
        if (metadataReader == null) {
            DebuggerLoggingService.LogMessage($"  The metadata of {moduleName} could not be read");
            ContinueProcess();
            return;
        }
        DebuggerLoggingService.LogMessage(metadataReader.HasSymbols ? $"  Symbols loaded for {moduleName}" : $"  No symbols found for {moduleName}");

        // EnC and disabled optimizations are only enabled for assemblies built by the user, which makes them the user code heuristic
        var jitFlags = corModule.GetJITCompilerFlags();
        var isUserCode = jitFlags == CorDebugJITCompilerFlags.CORDEBUG_JIT_DISABLE_OPTIMIZATION || jitFlags == CorDebugJITCompilerFlags.CORDEBUG_JIT_ENABLE_ENC;
        if (JustMyCode && isUserCode && metadataReader.HasSymbols)
            corModule.SetJMCStatus(true, 0, []);

        var module = new ModuleInfo(corModule, modulePath, metadataReader, isUserCode);
        modules[module.BaseAddress] = module;
        ModulesVersion++;

        TrySetEntryPointBreakpoint(module);
        // The expression evaluator needs the core library's primitive types, every stop happens after it is loaded
        if (moduleName == CoreLibraryName)
            evaluator = new ExpressionEvaluator(this, PrimitiveTypeClasses.Load(corModule));

        OnModuleLoaded?.Invoke(module);
        foreach (var breakpoint in breakpointManager.BindPending(module))
            OnBreakpointChanged?.Invoke(breakpoint);
        ContinueProcess();
    }

    private ModuleMetadataReader? LoadModuleMetadata(ICorDebugModule corModule, string modulePath) {
        try {
            if (!corModule.IsInMemory())
                return ModuleMetadataReader.TryLoad(modulePath);
            ArgumentNullException.ThrowIfNull(process);
            var (image, _) = process.ReadMemory(corModule.GetBaseAddress(), corModule.GetSize());
            return ModuleMetadataReader.TryLoad(image);
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"  Error loading the metadata of {Path.GetFileName(modulePath)}", ex);
            return null;
        }
    }
    // A 'stopAtEntry' launch places a one-shot breakpoint on the entry point of the first assembly that has one
    private void TrySetEntryPointBreakpoint(ModuleInfo module) {
        if (!stopAtEntryPending)
            return;
        var entryPointToken = module.MetadataReader.GetEntryPointToken();
        if (entryPointToken == null)
            return;

        try {
            var ilOffset = module.MetadataReader.ResolveMethodEntry(entryPointToken.Value)?.ILOffset ?? 0;
            var function = module.Module.GetFunctionFromToken(entryPointToken.Value);
            var breakpoint = function.GetILCode().CreateBreakpoint(ilOffset);
            breakpoint.Activate(true);
            entryPointBreakpoint = breakpoint;
            stopAtEntryPending = false;
            DebuggerLoggingService.LogMessage($"Entry point breakpoint set in {module.Name} at method 0x{entryPointToken.Value:X}, IL offset {ilOffset}");
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Failed to set the entry point breakpoint", ex);
        }
    }
    private void ClearEntryPointBreakpoint() {
        entryPointBreakpoint?.TryActivate(false);
        entryPointBreakpoint = null;
    }
}
