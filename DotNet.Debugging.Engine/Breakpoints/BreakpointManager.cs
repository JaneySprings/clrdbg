using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Metadata;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Breakpoints;

// Owns the breakpoints requested by the client and binds them to the debuggee's code as modules load
internal class BreakpointManager {
    private readonly Dictionary<int, Breakpoint> breakpoints = new Dictionary<int, Breakpoint>();
    private int nextId = 1;

    public IEnumerable<Breakpoint> Breakpoints => breakpoints.Values;

    // Replaces the breakpoints of a file. Without a running process they stay pending until modules load
    public List<Breakpoint> SetBreakpoints(string filePath, List<BreakpointRequest> requests, IReadOnlyCollection<ModuleInfo> modules, bool hasProcess, bool requireExactSource, bool justMyCode) {
        foreach (var existing in breakpoints.Values.Where(it => !it.IsFunctionBreakpoint && it.FilePath == filePath).ToList()) {
            Deactivate(existing);
            breakpoints.Remove(existing.Id);
        }

        var result = new List<Breakpoint>();
        foreach (var request in requests) {
            var breakpoint = new Breakpoint(nextId++, filePath, request);
            breakpoints[breakpoint.Id] = breakpoint;
            if (!hasProcess)
                breakpoint.SetStatus(BreakpointStatus.Pending);
            else
                TryBind(breakpoint, modules, requireExactSource, justMyCode);
            result.Add(breakpoint);
        }
        return result;
    }
    public List<Breakpoint> SetFunctionBreakpoints(List<FunctionBreakpointRequest> requests, IReadOnlyCollection<ModuleInfo> modules, bool hasProcess) {
        foreach (var existing in breakpoints.Values.Where(it => it.IsFunctionBreakpoint).ToList()) {
            Deactivate(existing);
            breakpoints.Remove(existing.Id);
        }

        var result = new List<Breakpoint>();
        foreach (var request in requests) {
            var breakpoint = new Breakpoint(nextId++, request);
            breakpoints[breakpoint.Id] = breakpoint;
            try {
                var pattern = FunctionBreakpointPattern.Parse(request.Name);
                foreach (var module in modules)
                    TryBindFunction(breakpoint, module, pattern);
                if (!breakpoint.Verified)
                    breakpoint.SetStatus(hasProcess ? BreakpointStatus.NoMatchingFunctions : BreakpointStatus.Pending);
            }
            catch (ArgumentException ex) {
                breakpoint.SetStatus(BreakpointStatus.Error, ex.Message);
            }
            result.Add(breakpoint);
        }
        return result;
    }

    // Every breakpoint bound through the runtime breakpoint: the ones resolving to one location share it
    public List<Breakpoint> FindByCorBreakpoint(ICorDebugFunctionBreakpoint corBreakpoint) {
        return breakpoints.Values.Where(it => it.Bindings.Any(binding => binding.CorBreakpoint == corBreakpoint)).ToList();
    }
    // The process exists now, so pending breakpoints are no longer waiting for the debugging to start
    public List<Breakpoint> MarkProcessStarted() {
        foreach (var breakpoint in breakpoints.Values) {
            if (breakpoint.Status == BreakpointStatus.Pending)
                breakpoint.SetStatus(BreakpointStatus.NotProcessed);
        }
        return breakpoints.Values.ToList();
    }
    // Binds what the newly loaded module can resolve, returns the breakpoints whose reported status changed
    public List<Breakpoint> BindPending(ModuleInfo module, bool requireExactSource, bool justMyCode) {
        var changed = new List<Breakpoint>();
        if (!module.HasSymbols)
            return changed;

        foreach (var breakpoint in breakpoints.Values) {
            if (breakpoint.IsFunctionBreakpoint) {
                if (TryBindFunction(breakpoint, module))
                    changed.Add(breakpoint);
            }
            else if (!breakpoint.Verified) {
                var statusBefore = breakpoint.Status;
                if (TryBind(breakpoint, [module], requireExactSource, justMyCode))
                    changed.Add(breakpoint);
                // A rejection by the module is reported too (an equally named source that did not match, a method that
                // takes no breakpoint), a module without the document is not
                else if (breakpoint.Status != statusBefore && breakpoint.Status is BreakpointStatus.SourceMismatch or BreakpointStatus.InHiddenMethod or BreakpointStatus.InStepThroughMethod)
                    changed.Add(breakpoint);
            }
            // Microsoft's debugger moves a binding made by file name alone to a module whose document matches exactly
            else if (!breakpoint.IsExactMatch && TryRebind(breakpoint, module, requireExactSource, justMyCode)) {
                changed.Add(breakpoint);
            }
        }
        return changed;
    }
    public void Clear() {
        foreach (var breakpoint in breakpoints.Values)
            Deactivate(breakpoint);
        breakpoints.Clear();
        nextId = 1;
    }

    private bool TryBind(Breakpoint breakpoint, IReadOnlyCollection<ModuleInfo> modules, bool requireExactSource, bool justMyCode) {
        try {
            ModuleInfo? targetModule = null;
            ModuleInfo? mismatchModule = null;
            ResolvedBreakpoint? resolved = null;
            foreach (var module in modules) {
                if (!module.HasSymbols)
                    continue;
                var candidate = module.MetadataReader.ResolveBreakpoint(breakpoint.FilePath!, breakpoint.RequestedLine, breakpoint.RequestedColumn, requireExactSource, out var sourceMismatch);
                if (candidate == null) {
                    if (sourceMismatch && mismatchModule == null)
                        mismatchModule = module;
                    continue;
                }
                // The first match wins unless a later module matches the document exactly (path or content)
                if (resolved == null || (candidate.IsExactMatch && !resolved.IsExactMatch)) {
                    targetModule = module;
                    resolved = candidate;
                }
                if (resolved.IsExactMatch)
                    break;
            }
            if (targetModule == null || resolved == null) {
                if (mismatchModule != null) {
                    breakpoint.SourceMismatchModule = mismatchModule.Name;
                    breakpoint.SetStatus(BreakpointStatus.SourceMismatch);
                }
                // A module without the document must not clear a mismatch reported by an earlier one
                else if (breakpoint.Status != BreakpointStatus.SourceMismatch) {
                    breakpoint.SetStatus(BreakpointStatus.NoSymbols);
                }
                return false;
            }

            var rejection = GetAttributeRejection(targetModule, resolved.MethodToken, justMyCode);
            if (rejection != null) {
                breakpoint.SetStatus(rejection.Value);
                return false;
            }
            Bind(breakpoint, targetModule, resolved);
            return true;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"Error binding breakpoint {breakpoint.Id} at {breakpoint.FilePath}:{breakpoint.Line}", ex);
            breakpoint.SetStatus(BreakpointStatus.Error, ex.Message);
            return false;
        }
    }
    // Upgrades a binding made by file name alone once a module matching the document exactly loads.
    // The loose binding stays active until then, so an already bound breakpoint keeps working
    private bool TryRebind(Breakpoint breakpoint, ModuleInfo module, bool requireExactSource, bool justMyCode) {
        try {
            var resolved = module.MetadataReader.ResolveBreakpoint(breakpoint.FilePath!, breakpoint.RequestedLine, breakpoint.RequestedColumn, requireExactSource, out _);
            if (resolved == null || !resolved.IsExactMatch || GetAttributeRejection(module, resolved.MethodToken, justMyCode) != null)
                return false;

            Bind(breakpoint, module, resolved);
            return true;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"Error rebinding breakpoint {breakpoint.Id} to {module.Name}", ex);
            return false;
        }
    }
    // A method the debugger must never stop in refuses the breakpoint: one marked [DebuggerHidden] (directly or through
    // its type) always, one marked [DebuggerStepThrough] under Just My Code - the way Microsoft's debugger has it
    private static BreakpointStatus? GetAttributeRejection(ModuleInfo module, int methodToken, bool justMyCode) {
        var metadataImport = module.Module.GetMetaDataInterface<IMetaDataImport>();
        var method = new MethodDefToken((uint)methodToken);
        if (metadataImport.HasMethodOrTypeAttribute(method, AttributeNames.DebuggerHidden))
            return BreakpointStatus.InHiddenMethod;
        if (justMyCode && metadataImport.HasMethodOrTypeAttribute(method, AttributeNames.DebuggerStepThrough))
            return BreakpointStatus.InStepThroughMethod;
        return null;
    }
    // A rebind replaces the previous loose binding rather than keeping both active
    private void Bind(Breakpoint breakpoint, ModuleInfo module, ResolvedBreakpoint resolved) {
        Deactivate(breakpoint);
        breakpoint.Bindings.Clear();
        breakpoint.Bindings.Add(CreateBinding(module.Module, resolved));
        breakpoint.Location = resolved.Location;
        breakpoint.IsExactMatch = resolved.IsExactMatch;
        breakpoint.SetStatus(BreakpointStatus.Bound);
        DebuggerLoggingService.LogMessage($"Breakpoint {breakpoint.Id} bound at {resolved.Location.FilePath}:{breakpoint.Line} -> IL offset {resolved.ILOffset} in method 0x{resolved.MethodToken:X}");
    }
    private bool TryBindFunction(Breakpoint breakpoint, ModuleInfo module) {
        try {
            return TryBindFunction(breakpoint, module, FunctionBreakpointPattern.Parse(breakpoint.FunctionName!));
        }
        catch (ArgumentException) {
            return false;
        }
    }
    // Binds every matching method of the module, returns whether the breakpoint became verified by that
    private bool TryBindFunction(Breakpoint breakpoint, ModuleInfo module, FunctionBreakpointPattern pattern) {
        if (!module.HasSymbols)
            return false;

        var wasVerified = breakpoint.Verified;
        try {
            foreach (var resolved in FunctionBreakpointResolver.Resolve(module.MetadataReader, pattern)) {
                if (breakpoint.Bindings.Any(it => it.Module == module.Module && it.MethodToken == resolved.MethodToken))
                    continue;
                breakpoint.Bindings.Add(CreateBinding(module.Module, resolved));
                breakpoint.Location ??= resolved.Location;
                breakpoint.SetStatus(BreakpointStatus.Bound);
            }
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"Error binding function breakpoint '{breakpoint.FunctionName}' in {module.Name}", ex);
            if (!breakpoint.Verified)
                breakpoint.SetStatus(BreakpointStatus.Error, ex.Message);
        }
        return !wasVerified && breakpoint.Verified;
    }
    // Breakpoints resolving to one location (a source breakpoint on a method's first line and a function breakpoint on
    // the method, two breakpoints on one statement) share one runtime breakpoint: the runtime then reports the location
    // once, naming all of them, rather than once per breakpoint with a continue needed for each
    private BreakpointBinding CreateBinding(ICorDebugModule module, ResolvedBreakpoint resolved) {
        var corBreakpoint = breakpoints.Values.SelectMany(it => it.Bindings).FirstOrDefault(it => it.IsAt(module, resolved.MethodToken, resolved.ILOffset))?.CorBreakpoint;
        if (corBreakpoint == null) {
            corBreakpoint = module.GetFunctionFromToken(resolved.MethodToken).GetILCode().CreateBreakpoint(resolved.ILOffset);
            corBreakpoint.Activate(true);
        }
        return new BreakpointBinding(corBreakpoint, module, resolved.MethodToken, resolved.ILOffset);
    }
    // A runtime breakpoint another breakpoint still shares stays active; a process that is gone has nothing left to deactivate
    private void Deactivate(Breakpoint breakpoint) {
        foreach (var binding in breakpoint.Bindings) {
            if (FindByCorBreakpoint(binding.CorBreakpoint).Any(it => it != breakpoint))
                continue;
            var result = binding.CorBreakpoint.TryActivate(false);
            if (result == Cor.CORDBG_E_PROCESS_TERMINATED)
                return;
            if (result != Cor.S_OK)
                DebuggerLoggingService.LogMessage($"Failed to deactivate breakpoint {breakpoint.Id}: 0x{result:X8}");
        }
    }
}
