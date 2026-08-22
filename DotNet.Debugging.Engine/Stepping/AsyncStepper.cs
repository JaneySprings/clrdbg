using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Models;
using DotNet.Debugging.Engine.Variables;

namespace DotNet.Debugging.Engine.Stepping;

internal enum AsyncBreakpointResult {
    // The breakpoint is not one of the async stepper's
    NotHandled,
    // The breakpoint served its purpose, keep running
    Continue,
    // The awaited task completed, step out of the runtime's notification method to land in the resumed method
    StepOut,
}

// Steps across 'await' points. A plain step over an await would run until the method returns to its caller,
// so the step is carried by a breakpoint on the yield point and then one on the resume point instead
// (https://github.com/dotnet/runtime/blob/main/docs/design/features/async-debugging.md)
internal class AsyncStepper {
    private const string NotifyDebuggerOfWaitCompletionMethod = "NotifyDebuggerOfWaitCompletion";
    private const string TaskTypeName = "System.Threading.Tasks.Task";
    private const string AsyncVoidBuilderTypeName = "System.Runtime.CompilerServices.AsyncVoidMethodBuilder";

    private readonly ManagedDebugger debugger;
    private readonly StepController stepController;
    private AsyncStep? currentStep;
    private AsyncBreakpoint? notifyDebuggerBreakpoint;

    public AsyncStepper(ManagedDebugger debugger, StepController stepController) {
        this.debugger = debugger;
        this.stepController = stepController;
    }

    // Returns true when the step is carried entirely by breakpoints and no plain stepper is needed
    public async Task<bool> TrySetupAsync(ICorDebugThread thread, StepKind kind) {
        if (thread.GetActiveFrame() is not ICorDebugILFrame frame)
            return false;

        var function = frame.GetFunction();
        var moduleAddress = function.GetModule().GetBaseAddress();
        var methodToken = function.GetToken();
        var module = debugger.FindModule(function.GetModule());
        if (module == null || !module.HasSymbols)
            return false;

        var asyncInfo = module.MetadataReader.GetAsyncMethodInfo(methodToken);
        if (asyncInfo == null)
            return false;

        // Past the last statement of an async method the only way forward is out of it
        var ip = frame.GetIP();
        var isInPrologOrEpilog = ip.pMappingResult == CorDebugMappingResult.MAPPING_PROLOG || ip.pMappingResult == CorDebugMappingResult.MAPPING_EPILOG;
        if (kind != StepKind.Out && !isInPrologOrEpilog && ip.pnOffset >= asyncInfo.LastUserCodeOffset)
            kind = StepKind.Out;

        ClearActiveStep();
        if (kind == StepKind.Out)
            return await TrySetupStepOutAsync(thread, frame);

        var awaitInfo = FindNextAwait(asyncInfo, (uint)ip.pnOffset);
        if (awaitInfo == null)
            return false;

        var yieldBreakpoint = function.GetILCode().CreateBreakpoint((int)awaitInfo.YieldOffset);
        yieldBreakpoint.Activate(true);
        currentStep = new AsyncStep(thread.GetId(), kind, awaitInfo.ResumeOffset, new AsyncBreakpoint(yieldBreakpoint, moduleAddress, methodToken, awaitInfo.YieldOffset));
        // The plain stepper still runs, it stops the step when the method leaves before reaching the yield point
        return false;
    }
    public async Task<AsyncBreakpointResult> TryHandleBreakpointAsync(ICorDebugThread thread, ICorDebugFunctionBreakpoint breakpoint) {
        if (notifyDebuggerBreakpoint != null && notifyDebuggerBreakpoint.Matches(breakpoint, thread)) {
            notifyDebuggerBreakpoint.Deactivate();
            notifyDebuggerBreakpoint = null;
            return AsyncBreakpointResult.StepOut;
        }
        if (currentStep == null)
            return AsyncBreakpointResult.NotHandled;

        // Any other breakpoint cancels the async step
        if (!currentStep.Breakpoint.Matches(breakpoint, thread) || thread.GetActiveFrame() is not ICorDebugILFrame frame || frame.GetIP().pnOffset != currentStep.Breakpoint.ILOffset) {
            ClearActiveStep();
            return AsyncBreakpointResult.NotHandled;
        }

        if (currentStep.Status == AsyncStepStatus.YieldBreakpoint) {
            if (currentStep.ThreadId != thread.GetId())
                return AsyncBreakpointResult.NotHandled;
            await HandleYieldBreakpointAsync(thread, frame);
            return AsyncBreakpointResult.Continue;
        }
        await HandleResumeBreakpointAsync(thread, frame);
        return AsyncBreakpointResult.Continue;
    }

    public void ClearActiveStep() {
        currentStep?.Dispose();
        currentStep = null;
    }
    public void Disable() {
        ClearActiveStep();
        notifyDebuggerBreakpoint?.Deactivate();
        notifyDebuggerBreakpoint = null;
    }

    // Stepping out of an async method means waiting for its task: the builder is asked to notify the debugger when the
    // awaiting code resumes, which calls Task.NotifyDebuggerOfWaitCompletion where a breakpoint catches it
    private async Task<bool> TrySetupStepOutAsync(ICorDebugThread thread, ICorDebugILFrame frame) {
        var builder = GetAsyncBuilder(frame);
        if (builder == null)
            return false;
        // An async void method has no task to wait for, a plain step out works there
        if (TypeNameFormatter.GetTypeName(builder.GetExactType()) == AsyncVoidBuilderTypeName)
            return false;
        if (!await SetNotificationForWaitCompletionAsync(builder, thread))
            return false;
        return SetupNotifyDebuggerBreakpoint();
    }
    private async Task<bool> SetNotificationForWaitCompletionAsync(ICorDebugValue builder, ICorDebugThread thread) {
        try {
            var objectValue = builder.UnwrapDebugValueToObject();
            var corClass = objectValue.GetClass();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            var methodDef = metadataImport.FindMethod(corClass.GetToken(), "SetNotificationForWaitCompletion", 0, 0);
            if (methodDef.IsNil)
                return false;

            var eval = thread.CreateEval();
            var enabled = CreateBooleanValue(eval, true);
            var function = corClass.GetModule().GetFunctionFromToken(methodDef);
            var result = await debugger.FuncEval.CallFunctionAsync(eval, function, objectValue.GetExactType().GetTypeParameters(), [builder, enabled]);
            return result == null;
        }
        catch {
            return false;
        }
    }
    private bool SetupNotifyDebuggerBreakpoint() {
        try {
            var coreLib = debugger.Modules.FirstOrDefault(it => it.Name == ManagedDebugger.CoreLibraryName);
            if (coreLib == null)
                return false;

            var metadataImport = coreLib.Module.GetMetaDataInterface<IMetaDataImport>();
            var taskType = metadataImport.FindTypeDef(TaskTypeName, MetadataToken.Nil);
            if (taskType == null)
                return false;
            var methodDef = metadataImport.FindMethod(taskType.Value, NotifyDebuggerOfWaitCompletionMethod, 0, 0);
            if (methodDef.IsNil)
                return false;

            var function = coreLib.Module.GetFunctionFromToken(methodDef);
            var breakpoint = function.GetILCode().CreateBreakpoint(0);
            breakpoint.Activate(true);
            notifyDebuggerBreakpoint = new AsyncBreakpoint(breakpoint, coreLib.BaseAddress, methodDef, 0);
            return true;
        }
        catch {
            return false;
        }
    }

    // The method yielded: the plain stepper is done and the resume point is awaited, possibly on another thread
    private async Task HandleYieldBreakpointAsync(ICorDebugThread thread, ICorDebugILFrame frame) {
        var step = currentStep!;
        stepController.CancelStep();

        // The builder's id tells the resumed invocation apart from other invocations of the same method
        var function = frame.GetFunction();
        step.AsyncIdHandle = await GetAsyncIdAsync(frame) ?? throw new InvalidOperationException("The async method builder has no debugger id");

        var resumeBreakpoint = function.GetILCode().CreateBreakpoint((int)step.ResumeOffset);
        resumeBreakpoint.Activate(true);
        step.Breakpoint.Deactivate();
        step.Breakpoint = new AsyncBreakpoint(resumeBreakpoint, function.GetModule().GetBaseAddress(), function.GetToken(), step.ResumeOffset);
        step.Status = AsyncStepStatus.ResumeBreakpoint;
    }
    private async Task HandleResumeBreakpointAsync(ICorDebugThread thread, ICorDebugILFrame frame) {
        var step = currentStep!;
        var isSameInvocation = step.ThreadId == thread.GetId();
        if (!isSameInvocation && step.AsyncIdHandle != null) {
            var asyncId = await GetAsyncIdAsync(frame);
            if (asyncId != null) {
                var currentAddress = asyncId.Dereference().GetAddress();
                var storedAddress = step.AsyncIdHandle.Dereference().GetAddress();
                isSameInvocation = currentAddress == storedAddress || currentAddress.Value == 0 || storedAddress.Value == 0;
            }
        }
        if (!isSameInvocation)
            return;

        // The method resumed, the rest of the step is an ordinary one from here
        stepController.CreateStepper(thread, step.Kind);
        ClearActiveStep();
    }

    private static AwaitInfo? FindNextAwait(AsyncMethodInfo asyncInfo, uint currentOffset) {
        foreach (var awaitInfo in asyncInfo.Awaits) {
            if (currentOffset <= awaitInfo.YieldOffset)
                return awaitInfo;
            // Inside an await block there is no next await to step to
            if (currentOffset < awaitInfo.ResumeOffset)
                break;
        }
        return null;
    }
    private async Task<ICorDebugHandleValue?> GetAsyncIdAsync(ICorDebugILFrame frame) {
        var builder = GetAsyncBuilder(frame);
        if (builder == null)
            return null;
        var objectId = await debugger.FuncEval.GetPropertyValueAsync(builder, frame, "ObjectIdForDebugger");
        return objectId as ICorDebugHandleValue;
    }
    // The '<>t__builder' field of the state machine 'this'
    private static ICorDebugValue? GetAsyncBuilder(ICorDebugILFrame frame) {
        try {
            var function = frame.GetFunction();
            var metadataImport = function.GetModule().GetMetaDataInterface<IMetaDataImport>();
            if (metadataImport.GetMethodProps(function.GetToken()).pdwAttr.IsMdStatic())
                return null;

            var arguments = frame.GetArguments();
            if (arguments.Length == 0 || arguments[0] is not ICorDebugReferenceValue thisReference || thisReference.IsNull())
                return null;
            if (thisReference.Dereference() is not ICorDebugObjectValue thisObject)
                return null;

            var thisClass = thisObject.GetClass();
            var fieldDef = metadataImport.EnumFieldsWithName(thisClass.GetToken(), "<>t__builder").SingleOrDefault();
            if (fieldDef.IsNil)
                return null;
            return thisObject.GetFieldValue(thisClass, fieldDef).UnwrapDebugValue();
        }
        catch {
            return null;
        }
    }
    private static ICorDebugValue CreateBooleanValue(ICorDebugEval eval, bool value) {
        var corValue = eval.CreateValue(CorElementType.BOOLEAN, null);
        if (value && corValue is ICorDebugGenericValue genericValue)
            genericValue.SetValueFromBytes([1]);
        return corValue;
    }

    private enum AsyncStepStatus {
        YieldBreakpoint,
        ResumeBreakpoint,
    }

    private class AsyncBreakpoint {
        private readonly ICorDebugFunctionBreakpoint breakpoint;
        private readonly CordbAddress moduleAddress;
        private readonly MethodDefToken methodToken;

        public uint ILOffset { get; }

        public AsyncBreakpoint(ICorDebugFunctionBreakpoint breakpoint, CordbAddress moduleAddress, MethodDefToken methodToken, uint ilOffset) {
            this.breakpoint = breakpoint;
            this.moduleAddress = moduleAddress;
            this.methodToken = methodToken;
            ILOffset = ilOffset;
        }

        public bool Matches(ICorDebugFunctionBreakpoint hitBreakpoint, ICorDebugThread thread) {
            var frame = thread.GetActiveFrame();
            if (frame == null)
                return false;
            var function = frame.GetFunction();
            return function.GetModule().GetBaseAddress() == moduleAddress && function.GetToken() == methodToken && hitBreakpoint == breakpoint;
        }
        public void Deactivate() {
            breakpoint.TryActivate(false);
        }
    }

    private class AsyncStep : IDisposable {
        public int ThreadId { get; }
        public StepKind Kind { get; }
        public uint ResumeOffset { get; }
        public AsyncStepStatus Status { get; set; }
        public AsyncBreakpoint Breakpoint { get; set; }
        // A strong handle to the builder's ObjectIdForDebugger
        public ICorDebugHandleValue? AsyncIdHandle { get; set; }

        public AsyncStep(int threadId, StepKind kind, uint resumeOffset, AsyncBreakpoint breakpoint) {
            ThreadId = threadId;
            Kind = kind;
            ResumeOffset = resumeOffset;
            Breakpoint = breakpoint;
            Status = AsyncStepStatus.YieldBreakpoint;
        }

        public void Dispose() {
            Breakpoint.Deactivate();
            AsyncIdHandle?.TryDispose();
            AsyncIdHandle = null;
        }
    }
}
