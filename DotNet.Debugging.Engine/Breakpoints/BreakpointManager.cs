using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Metadata;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Breakpoints;

// Owns the breakpoints requested by the client and binds them to the debuggee's code as modules load
internal class BreakpointManager {
    private readonly Dictionary<int, Breakpoint> breakpoints = new Dictionary<int, Breakpoint>();
    private int nextId = 1;

    public IEnumerable<Breakpoint> Breakpoints => breakpoints.Values;

    // Replaces the breakpoints of a file. What the loaded modules cannot bind stays unbound until a module loads
    public List<Breakpoint> SetBreakpoints(string filePath, List<BreakpointRequest> requests, IReadOnlyCollection<ModuleInfo> modules, SourceFileMapper sourceFileMapper, bool requireExactSource) {
        foreach (var existing in breakpoints.Values.Where(it => !it.IsFunctionBreakpoint && it.FilePath == filePath).ToList()) {
            Deactivate(existing);
            breakpoints.Remove(existing.Id);
        }

        var result = new List<Breakpoint>();
        foreach (var request in requests) {
            var breakpoint = new Breakpoint(nextId++, filePath, request);
            breakpoints[breakpoint.Id] = breakpoint;
            TryBind(breakpoint, modules, sourceFileMapper, requireExactSource);
            result.Add(breakpoint);
        }
        return result;
    }
    public List<Breakpoint> SetFunctionBreakpoints(List<FunctionBreakpointRequest> requests, IReadOnlyCollection<ModuleInfo> modules) {
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
                    breakpoint.SetStatus(BreakpointStatus.Unbound);
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
    // Binds what the newly loaded module can resolve - a bound breakpoint too, when the module compiles its file as
    // well - and returns the breakpoints whose reported state changed
    public List<Breakpoint> BindPending(ModuleInfo module, SourceFileMapper sourceFileMapper, bool requireExactSource) {
        var changed = new List<Breakpoint>();
        if (!module.HasSymbols)
            return changed;

        foreach (var breakpoint in breakpoints.Values) {
            if (breakpoint.IsFunctionBreakpoint) {
                if (TryBindFunction(breakpoint, module))
                    changed.Add(breakpoint);
            }
            else if (TryBind(breakpoint, [module], sourceFileMapper, requireExactSource)) {
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

    // Binds the breakpoint in every module that resolves its file and line: a file compiled into several modules
    // (a linked file, an identical copy) is hit in each of them. Returns whether the reported state changed
    private bool TryBind(Breakpoint breakpoint, IReadOnlyCollection<ModuleInfo> modules, SourceFileMapper sourceFileMapper, bool requireExactSource) {
        var statusBefore = breakpoint.Status;
        var locationBefore = breakpoint.Location;
        var bindingsBefore = breakpoint.Bindings.Count;
        var noCodeFound = false;
        var sourceMismatchFound = false;
        try {
            foreach (var module in modules) {
                if (!module.HasSymbols || breakpoint.Bindings.Any(it => it.Module == module.Module))
                    continue;
                var resolved = module.MetadataReader.ResolveBreakpoint(breakpoint.FilePath!, breakpoint.RequestedLine, breakpoint.RequestedColumn, sourceFileMapper, requireExactSource, out var status);
                if (resolved == null) {
                    noCodeFound |= status == BreakpointStatus.NoCode;
                    sourceMismatchFound |= status == BreakpointStatus.SourceMismatch;
                    continue;
                }
                breakpoint.Bindings.Add(CreateBinding(module.Module, resolved));
                // The breakpoint reports the document matched by path when a module has one, the first binding until then
                if (breakpoint.Location == null || (resolved.MatchKind == DocumentMatchKind.Path && breakpoint.MatchKind != DocumentMatchKind.Path)) {
                    breakpoint.Location = resolved.Location;
                    breakpoint.MatchKind = resolved.MatchKind;
                }
                breakpoint.SetStatus(BreakpointStatus.Bound);
                DebuggerLoggingService.LogMessage($"Breakpoint {breakpoint.Id} bound at {resolved.Location.FilePath}:{breakpoint.Line} -> IL offset {resolved.ILOffset} in method 0x{resolved.MethodToken:X} of {module.Name}");
            }
            // The document found without code at the line is the answer; a differing source is one only when no
            // module has the file; a module without the document never clears what an earlier one reported
            if (!breakpoint.Verified) {
                if (noCodeFound)
                    breakpoint.SetStatus(BreakpointStatus.NoCode);
                else if (sourceMismatchFound && breakpoint.Status == BreakpointStatus.Unbound)
                    breakpoint.SetStatus(BreakpointStatus.SourceMismatch);
            }
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"Error binding breakpoint {breakpoint.Id} at {breakpoint.FilePath}:{breakpoint.Line}", ex);
            breakpoint.SetStatus(BreakpointStatus.Error, ex.Message);
        }
        return breakpoint.Status != statusBefore || breakpoint.Location != locationBefore || breakpoint.Bindings.Count != bindingsBefore;
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
