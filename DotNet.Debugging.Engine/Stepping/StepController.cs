using System.Diagnostics.CodeAnalysis;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Stepping;

// Drives the ICorDebugStepper of a step request and decides where a completed step stops
internal class StepController {
    private readonly ManagedDebugger debugger;
    private readonly AsyncStepper asyncStepper;
    private ICorDebugStepper? stepper;
    // The thread the stepper runs on: a step completes on that thread only, and only that thread's breakpoint can be its destination
    private int steppingThreadId;
    // The kind the user requested, a step continued past a filtered method resumes with it
    private StepKind userStepKind;
    // The last completed step left a filtered method behind and its continuation is running
    private bool isSkippingFilteredMethod;
    // The last completed step stopped in a hidden finally and its continuation is running: hidden code it
    // reaches next is still cleanup, even outside a handler (the plumbing between two nested finallys)
    private bool isCrossingHiddenFinally;
    // The statement the user's step started in, a filtered skip that returns into it resumes the step
    private ModuleInfo? stepStatementModule;
    private int stepStatementMethodToken;
    private int stepStatementStart;
    private int stepFrameDepth;
    private bool isStepSuspended;
    // A step that completed on another thread while an evaluation ran: the thread is held where it stands and the completion reported after the evaluation
    private ICorDebugThread? heldThread;
    private CorDebugStepReason heldReason;

    public bool IsStepping => stepper != null;
    // The runtime reports the step as done but the StepComplete callback is still queued behind the current one
    public bool IsStepComplete => stepper != null && !stepper.IsActive();
    public int SteppingThreadId => steppingThreadId;

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
        isCrossingHiddenFinally = false;
        isStepSuspended = false;
        stepFrameDepth = thread.GetFrameDepth();
        RememberStepStatement(frame);
        if (await asyncStepper.TrySetupAsync(thread, kind))
            return;
        CreateStepper(thread, kind);
    }
    public Task<AsyncBreakpointResult> TryHandleBreakpointAsync(ICorDebugThread thread, ICorDebugFunctionBreakpoint breakpoint) {
        return asyncStepper.TryHandleBreakpointAsync(thread, breakpoint);
    }
    // Whether a completed stepper is the one in progress: the completion of an abandoned one (cancelled when another
    // thread's breakpoint stopped, its callback already queued behind that breakpoint) still arrives with the next continue
    public bool OwnsStepper(ICorDebugStepper candidate) {
        return stepper != null && stepper == candidate;
    }
    // Cancels the stepper for an evaluation on its own thread (the hijacked evaluation and the stepper's patches would
    // share the thread), to be re-armed by ResumeSuspendedStep once the evaluation has decided not to stop
    public void SuspendStep() {
        if (stepper == null)
            return;
        CancelStep();
        isStepSuspended = true;
    }
    // Re-arms a suspended step. A breakpoint inside a stepped-over call leaves the thread deeper than the statement the
    // step started in: a step out marked as a skip returns into it, and TryCompleteStep steps the rest of the statement
    // from there. At the step's own depth (a step out passing a later breakpoint of its method) the user's kind goes on
    public void ResumeSuspendedStep(ICorDebugThread thread) {
        if (!isStepSuspended)
            return;
        isStepSuspended = false;
        if (thread.GetActiveFrame() is not ICorDebugILFrame) {
            DebuggerLoggingService.LogMessage($"The step suspended on thread {thread.GetId()} cannot be resumed, the active frame is not an IL frame");
            return;
        }
        if (thread.GetFrameDepth() > stepFrameDepth) {
            isSkippingFilteredMethod = true;
            CreateStepper(thread, StepKind.Out);
            return;
        }
        ResumeStep(thread, userStepKind);
    }
    // A step that completed while an evaluation runs cannot stop yet, and the debuggee has to run for the evaluation to end:
    // the completed thread is held where it stands (it does not run with the rest of the process) until the completion is
    // taken or dropped. False when the thread cannot be held, the caller drops the step then
    public bool TryHoldCompletion(ICorDebugThread thread, CorDebugStepReason reason) {
        ReleaseHeldThread();
        var result = thread.TrySetDebugState(CorDebugThreadState.THREAD_SUSPEND);
        if (result != Cor.S_OK) {
            DebuggerLoggingService.LogMessage($"Thread {thread.GetId()} cannot be held for its step completion: 0x{result:X8}");
            return false;
        }
        heldThread = thread;
        heldReason = reason;
        DebuggerLoggingService.LogMessage($"Step completed on thread {thread.GetId()} during an evaluation, the thread is held until the evaluation is over");
        return true;
    }
    // The completion held during an evaluation, its thread released to run again once the stop is reported or the step goes on
    public bool TryTakeHeldCompletion([NotNullWhen(true)] out ICorDebugThread? thread, out CorDebugStepReason reason) {
        thread = heldThread;
        reason = heldReason;
        ReleaseHeldThread();
        return thread != null;
    }

    // The location the completed step stops at, null when the step has to go on (another step is set up then)
    public bool TryCompleteStep(ICorDebugThread thread, CorDebugStepReason reason, out SourceLocation? location) {
        location = null;
        var wasSkippingFilteredMethod = isSkippingFilteredMethod;
        var wasCrossingHiddenFinally = isCrossingHiddenFinally;
        isSkippingFilteredMethod = false;
        isCrossingHiddenFinally = false;
        // An active async step means a breakpoint is waiting at the next yield/resume point and the plain step
        // got there first, so the method left before reaching the await
        asyncStepper.ClearActiveStep();
        CancelStep();

        if (thread.GetActiveFrame() is not ICorDebugILFrame frame)
            return true;

        var function = frame.GetFunction();
        var module = debugger.FindModule(function.GetModule());
        if (module == null || !module.HasSymbols) {
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
            ResumeStep(thread, StepKind.Into);
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
            ResumeStep(thread, StepKind.Into);
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
            ResumeStep(thread, StepKind.Over);
            return false;
        }
        // A step that came to rest in a hidden region goes on when the region is cleanup between two
        // statements: the finally a 'using' or a 'lock' compiles to (the runtime ends a range step at the
        // handler even though its offsets lie inside the range), the plumbing between two nested finallys
        // (which belongs to no handler, a crossing under way covers it), or the hoisted DisposeAsync of an
        // 'await using' or 'await foreach' (recognized by its await still lying ahead in the hidden code).
        // The step keeps the user's kind - a step into enters a Dispose call the region makes, the way
        // vsdbg does; a step out already left its frame and covers the region like a step over. Hidden
        // code past its await's resume point is different: that is where a step out of an async method
        // ends, the mapping reports the awaiting statement there, and such a stop stands
        if (module.MetadataReader.IsInHiddenRegion(methodToken, ip.pnOffset)
            && (wasCrossingHiddenFinally || module.MetadataReader.IsInFinallyHandler(methodToken, ip.pnOffset) || module.MetadataReader.HasAwaitAhead(methodToken, ip.pnOffset, nextStatementOffset))) {
            location = null;
            isCrossingHiddenFinally = true;
            ResumeStep(thread, userStepKind == StepKind.Out ? StepKind.Over : userStepKind);
            return false;
        }
        // A skipped method returned into the statement the user's step started from, the rest of the step remains.
        // The returned-to offset cannot tell how much of the statement is left (the runtime only maps it
        // approximately, snapped to the statement start), so the step simply covers the statement again
        if (wasSkippingFilteredMethod && reason == CorDebugStepReason.STEP_RETURN && IsInStepStatement(module, methodToken, ip.pnOffset)) {
            location = null;
            ResumeStep(thread, userStepKind);
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
                newStepper.StepRange(kind == StepKind.Into, [range]);
            }
            else {
                newStepper.Step(kind == StepKind.Into);
            }
        }

        stepper = newStepper;
        steppingThreadId = thread.GetId();
        DebuggerLoggingService.LogMessage($"Stepper created on thread {steppingThreadId}: {kind} at IL_{ilFrame.GetIP().pnOffset:X4}");
        return newStepper;
    }
    public void CancelStep() {
        stepper?.Deactivate();
        stepper = null;
        ReleaseHeldThread();
    }
    // Resumes an interrupted step: the async carry is armed anew first, so an await still ahead of the
    // resumed step (e.g. the hidden DisposeAsync a 'break' jumps to) carries it across the yield
    private void ResumeStep(ICorDebugThread thread, StepKind kind) {
        asyncStepper.ArmAwaitCarry(thread, kind);
        CreateStepper(thread, kind);
    }
    // Abandons every step in progress, on a pause or an exception
    public void Disable() {
        CancelStep();
        asyncStepper.Disable();
        isSkippingFilteredMethod = false;
        isCrossingHiddenFinally = false;
        isStepSuspended = false;
    }
    private void ReleaseHeldThread() {
        if (heldThread == null)
            return;
        var result = heldThread.TrySetDebugState(CorDebugThreadState.THREAD_RUN);
        if (result != Cor.S_OK)
            DebuggerLoggingService.LogMessage($"The held thread {heldThread.GetId()} could not be released: 0x{result:X8}");
        heldThread = null;
    }

    private void RememberStepStatement(ICorDebugILFrame frame) {
        stepStatementModule = null;
        stepStatementMethodToken = 0;
        stepStatementStart = -1;

        var function = frame.GetFunction();
        var module = debugger.FindModule(function.GetModule());
        if (module == null || !module.HasSymbols)
            return;
        if (!module.MetadataReader.TryGetStepRange(function.GetToken(), frame.GetIP().pnOffset, out var startOffset, out _))
            return;

        stepStatementModule = module;
        stepStatementMethodToken = function.GetToken();
        stepStatementStart = startOffset;
    }
    private bool IsInStepStatement(ModuleInfo module, int methodToken, int ilOffset) {
        if (module != stepStatementModule || methodToken != stepStatementMethodToken)
            return false;
        if (!module.MetadataReader.TryGetStepRange(methodToken, ilOffset, out var startOffset, out _))
            return false;
        return startOffset == stepStatementStart;
    }
}
