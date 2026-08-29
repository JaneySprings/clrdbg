using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Stepping;

// Drives the ICorDebugStepper of a step request and decides where a completed step stops
internal class StepController {
    private readonly ManagedDebugger debugger;
    private readonly AsyncStepper asyncStepper;
    private ICorDebugStepper? stepper;
    // The kind the user requested, a step continued past a filtered method resumes with it
    private StepKind userStepKind;
    // The last completed step left a filtered method behind and its continuation is running
    private bool isSkippingFilteredMethod;
    // The statement the user's step started in, a filtered skip that returns into it resumes the step
    private CordbAddress stepStatementModule;
    private int stepStatementMethodToken;
    private int stepStatementStart;

    public bool IsStepping => stepper != null;
    // The runtime reports the step as done but the StepComplete callback is still queued behind the current one
    public bool IsStepComplete => stepper != null && !stepper.IsActive();

    public StepController(ManagedDebugger debugger) {
        this.debugger = debugger;
        asyncStepper = new AsyncStepper(debugger, this);
    }

    // Sets the step up, the caller continues the debuggee
    public async Task StepAsync(ICorDebugThread thread, StepKind kind) {
        if (thread.GetActiveFrame() is not ICorDebugILFrame frame)
            throw new InvalidOperationException("The active frame is not an IL frame");
        if (stepper != null)
            throw new InvalidOperationException("A step operation is already in progress");

        userStepKind = kind;
        isSkippingFilteredMethod = false;
        RememberStepStatement(frame);
        if (await asyncStepper.TrySetupAsync(thread, kind))
            return;
        CreateStepper(thread, kind);
    }
    public Task<AsyncBreakpointResult> TryHandleBreakpointAsync(ICorDebugThread thread, ICorDebugFunctionBreakpoint breakpoint) {
        return asyncStepper.TryHandleBreakpointAsync(thread, breakpoint);
    }

    // The location the completed step stops at, null when the step has to go on (another step is set up then)
    public bool TryCompleteStep(ICorDebugThread thread, CorDebugStepReason reason, out SourceLocation? location) {
        location = null;
        var wasSkippingFilteredMethod = isSkippingFilteredMethod;
        isSkippingFilteredMethod = false;
        // An active async step means a breakpoint is waiting at the next yield/resume point and the plain step
        // got there first, so the method left before reaching the await
        asyncStepper.ClearActiveStep();
        CancelStep();

        if (thread.GetActiveFrame() is not ICorDebugILFrame frame)
            return true;

        var function = frame.GetFunction();
        var module = debugger.GetModule(function.GetModule());
        if (!module.HasSymbols) {
            // A step into a method without symbols (Just My Code off) leaves it right away, like a
            // filtered one - vsdbg does not stop where no source can be shown either
            if (reason == CorDebugStepReason.STEP_CALL) {
                isSkippingFilteredMethod = true;
                CreateStepper(thread, StepKind.Out);
                return false;
            }
            // Elsewhere there is no source to map the stop to, the client shows the frame as is
            return true;
        }

        location = debugger.GetSourceLocation(frame);
        if (location == null) {
            // A method with symbols but no source at this offset: compiler generated code (e.g. an async state machine) to step through
            CreateStepper(thread, StepKind.Into);
            return false;
        }

        var ip = frame.GetIP();
        if (ip.pMappingResult == CorDebugMappingResult.MAPPING_UNMAPPED_ADDRESS || ip.pMappingResult == CorDebugMappingResult.MAPPING_NO_INFO)
            throw new InvalidOperationException("The IL frame IP is unmapped or has no mapping info");

        var methodToken = function.GetToken();
        var metadataImport = function.GetModule().GetMetaDataInterface<IMetaDataImport>();
        // A method the debugger must not stop in ([DebuggerStepThrough] and friends): keep stepping
        // into it, so the step lands in the first user code it calls or leaves it altogether
        if (metadataImport.IsNonUserMethod(methodToken, debugger.JustMyCode)) {
            location = null;
            isSkippingFilteredMethod = true;
            CreateStepper(thread, StepKind.Into);
            return false;
        }
        // Step filtering: a step into a property accessor or an operator leaves it right away
        if (reason == CorDebugStepReason.STEP_CALL && debugger.EnableStepFiltering && metadataImport.IsPropertyOrOperator(methodToken)) {
            location = null;
            isSkippingFilteredMethod = true;
            CreateStepper(thread, StepKind.Out);
            return false;
        }

        var nextStatementOffset = module.MetadataReader.GetNextSequencePointOffset(methodToken, ip.pnOffset);
        // A step into a call lands before the first statement of the callee, step over the prolog to reach it
        if (reason == CorDebugStepReason.STEP_CALL && ip.pnOffset < nextStatementOffset) {
            CreateStepper(thread, StepKind.Over);
            return false;
        }
        // A skipped method returned into the statement the user's step started from, the rest of the step remains.
        // The returned-to offset cannot tell how much of the statement is left (the runtime only maps it
        // approximately, snapped to the statement start), so the step simply covers the statement again
        if (wasSkippingFilteredMethod && reason == CorDebugStepReason.STEP_RETURN && IsInStepStatement(module, methodToken, ip.pnOffset)) {
            location = null;
            CreateStepper(thread, userStepKind);
            return false;
        }
        return true;
    }

    public ICorDebugStepper CreateStepper(ICorDebugThread thread, StepKind kind) {
        var frame = thread.GetActiveFrame();
        if (frame is not ICorDebugILFrame ilFrame)
            throw new InvalidOperationException("The active frame is not an IL frame");
        if (stepper != null)
            throw new InvalidOperationException("A step operation is already in progress");

        var newStepper = frame.CreateStepper();
        newStepper.SetInterceptMask(CorDebugIntercept.INTERCEPT_ALL & ~(CorDebugIntercept.INTERCEPT_SECURITY | CorDebugIntercept.INTERCEPT_CLASS_INIT));
        newStepper.SetUnmappedStopMask(CorDebugUnmappedStop.STOP_NONE);
        if (debugger.JustMyCode)
            newStepper.SetJMC(true);

        if (kind == StepKind.Out) {
            newStepper.StepOut();
        }
        else {
            // Step the whole statement, not just the current IL instruction
            var function = frame.GetFunction();
            var module = debugger.GetModule(function.GetModule());
            if (module.MetadataReader.TryGetStepRange(function.GetToken(), ilFrame.GetIP().pnOffset, out var startOffset, out var endOffset)) {
                if (startOffset == endOffset)
                    endOffset = function.GetILCode().GetSize();
                var range = new CorDebugStepRange { startOffset = checked((uint)startOffset), endOffset = checked((uint)endOffset) };
                newStepper.StepRange(kind == StepKind.Into, [range], 1);
            }
            else {
                newStepper.Step(kind == StepKind.Into);
            }
        }

        stepper = newStepper;
        return newStepper;
    }
    public void CancelStep() {
        stepper?.Deactivate();
        stepper = null;
    }
    // Abandons every step in progress, on a pause or an exception
    public void Disable() {
        CancelStep();
        asyncStepper.Disable();
        isSkippingFilteredMethod = false;
    }

    private void RememberStepStatement(ICorDebugILFrame frame) {
        stepStatementModule = new CordbAddress(0);
        stepStatementMethodToken = 0;
        stepStatementStart = -1;

        var function = frame.GetFunction();
        var module = debugger.FindModule(function.GetModule());
        if (module == null || !module.HasSymbols)
            return;
        if (!module.MetadataReader.TryGetStepRange(function.GetToken(), frame.GetIP().pnOffset, out var startOffset, out _))
            return;

        stepStatementModule = module.BaseAddress;
        stepStatementMethodToken = function.GetToken();
        stepStatementStart = startOffset;
    }
    private bool IsInStepStatement(ModuleInfo module, int methodToken, int ilOffset) {
        if (module.BaseAddress != stepStatementModule || methodToken != stepStatementMethodToken)
            return false;
        if (!module.MetadataReader.TryGetStepRange(methodToken, ilOffset, out var startOffset, out _))
            return false;
        return startOffset == stepStatementStart;
    }
}
