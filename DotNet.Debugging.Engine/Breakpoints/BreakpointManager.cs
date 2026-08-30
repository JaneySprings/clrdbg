using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Breakpoints;

// Owns the breakpoints requested by the client and binds them to the debuggee's code as modules load
internal class BreakpointManager {
    private readonly Dictionary<int, Breakpoint> breakpoints = new Dictionary<int, Breakpoint>();
    private int nextId = 1;

    public IEnumerable<Breakpoint> Breakpoints => breakpoints.Values;

    // Replaces the breakpoints of a file. Without a running process they stay pending until modules load
    public List<Breakpoint> SetBreakpoints(string filePath, List<BreakpointRequest> requests, IReadOnlyCollection<ModuleInfo> modules, bool hasProcess, bool requireExactSource) {
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
                TryBind(breakpoint, modules, requireExactSource);
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

    public Breakpoint? FindByCorBreakpoint(ICorDebugFunctionBreakpoint corBreakpoint) {
        foreach (var breakpoint in breakpoints.Values) {
            if (breakpoint.CorBreakpoint == corBreakpoint)
                return breakpoint;
            if (breakpoint.FunctionBindings.Any(it => it.CorBreakpoint == corBreakpoint))
                return breakpoint;
        }
        return null;
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
    public List<Breakpoint> BindPending(ModuleInfo module, bool requireExactSource) {
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
                if (TryBind(breakpoint, [module], requireExactSource))
                    changed.Add(breakpoint);
                // Microsoft's debugger also reports a breakpoint rejected because an equally named source did not match
                else if (breakpoint.Status == BreakpointStatus.SourceMismatch && statusBefore != BreakpointStatus.SourceMismatch)
                    changed.Add(breakpoint);
            }
            // Microsoft's debugger moves a binding made by file name alone to a module whose document matches exactly
            else if (breakpoint.ResolvedLocation != null && !breakpoint.ResolvedLocation.IsExactMatch) {
                if (TryRebind(breakpoint, module, requireExactSource))
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

    // '10' or '==10': break on the 10th hit, '>=10', '>10', '<=10', '<10', '%10': break every 10th hit
    public static bool CheckHitCondition(int hitCount, string hitCondition) {
        var condition = hitCondition.Trim().AsSpan();
        if (condition.StartsWith(">="))
            return int.TryParse(condition.Slice(2), out var threshold) && hitCount >= threshold;
        if (condition.StartsWith("<="))
            return int.TryParse(condition.Slice(2), out var threshold) && hitCount <= threshold;
        if (condition.StartsWith("=="))
            return int.TryParse(condition.Slice(2), out var target) && hitCount == target;
        if (condition.StartsWith('>'))
            return int.TryParse(condition.Slice(1), out var threshold) && hitCount > threshold;
        if (condition.StartsWith('<'))
            return int.TryParse(condition.Slice(1), out var threshold) && hitCount < threshold;
        if (condition.StartsWith('%'))
            return int.TryParse(condition.Slice(1), out var modulo) && modulo > 0 && hitCount % modulo == 0;
        return int.TryParse(condition, out var count) && hitCount == count;
    }

    private bool TryBind(Breakpoint breakpoint, IReadOnlyCollection<ModuleInfo> modules, bool requireExactSource) {
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
    private bool TryRebind(Breakpoint breakpoint, ModuleInfo module, bool requireExactSource) {
        try {
            var resolved = module.MetadataReader.ResolveBreakpoint(breakpoint.FilePath!, breakpoint.RequestedLine, breakpoint.RequestedColumn, requireExactSource, out _);
            if (resolved == null || !resolved.IsExactMatch)
                return false;

            Bind(breakpoint, module, resolved);
            return true;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"Error rebinding breakpoint {breakpoint.Id} to {module.Name}", ex);
            return false;
        }
    }
    private void Bind(Breakpoint breakpoint, ModuleInfo module, ResolvedBreakpoint resolved) {
        var function = module.Module.GetFunctionFromToken(resolved.MethodToken);
        var corBreakpoint = function.GetILCode().CreateBreakpoint(resolved.ILOffset);
        corBreakpoint.Activate(true);
        // A rebind replaces the previous loose binding rather than keeping both active
        breakpoint.CorBreakpoint?.TryActivate(false);

        breakpoint.CorBreakpoint = corBreakpoint;
        breakpoint.ResolvedLocation = resolved;
        breakpoint.Line = resolved.Location.Line;
        breakpoint.Column = resolved.Location.Column;
        breakpoint.EndLine = resolved.Location.EndLine;
        breakpoint.EndColumn = resolved.Location.EndColumn;
        breakpoint.Location = resolved.Location;
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
                if (breakpoint.FunctionBindings.Any(it => it.ModuleBaseAddress == module.BaseAddress && it.MethodToken == resolved.MethodToken))
                    continue;
                var function = module.Module.GetFunctionFromToken(resolved.MethodToken);
                var corBreakpoint = function.GetILCode().CreateBreakpoint(resolved.ILOffset);
                corBreakpoint.Activate(true);
                breakpoint.FunctionBindings.Add(new FunctionBreakpointBinding(corBreakpoint, module.BaseAddress, resolved.MethodToken));
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
    private void Deactivate(Breakpoint breakpoint) {
        var corBreakpoints = breakpoint.FunctionBindings.Select(it => it.CorBreakpoint).ToList();
        if (breakpoint.CorBreakpoint != null)
            corBreakpoints.Add(breakpoint.CorBreakpoint);

        foreach (var corBreakpoint in corBreakpoints) {
            var result = corBreakpoint.TryActivate(false);
            if (result == Cor.CORDBG_E_PROCESS_TERMINATED)
                return;
            if (result != Cor.S_OK)
                DebuggerLoggingService.LogMessage($"Failed to deactivate breakpoint {breakpoint.Id}: 0x{result:X8}");
        }
    }
}
