using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Interop;

// Obtains an ICorDebug for a local or remote process through dbgshim
// Originally based on https://github.com/lordmilko/ClrDebug/blob/5f46218f4b840ab8a94920623dc263b5f2334138/Samples/NetCore/Program.cs
internal static class DbgShimHost {
    private static RuntimeStartupRegistration? runtimeStartupRegistration;

    // Registers for the runtime startup of 'processId' and runs 'attach' (Initialize, SetManagedHandler, DebugActiveProcess)
    // from inside dbgshim's startup callback. That placement is the point: dbgshim lets the parked runtime continue the
    // moment the callback returns. On Unix the runtime then marks itself debugger-attached and blocks again until the
    // debugger continues, so a late attach is harmless - but on Windows nothing holds it, and a debuggee that finishes
    // quickly can be exiting by the time a post-callback DebugActiveProcess reaches it (E_ACCESSDENIED).
    // The registration is made synchronously, so a caller can resume a suspended runtime right after this returns its task;
    // a caller that then cannot resume it cancels the wait, which withdraws the registration
    public static async Task AttachAsync(int processId, Action<ICorDebug> attach, CancellationToken cancellationToken = default) {
        if (runtimeStartupRegistration != null)
            throw new InvalidOperationException("A runtime startup registration is already in progress");

        // The continuation must leave dbgshim's helper thread: 'UnregisterForRuntimeStartup' below waits for that thread to finish
        var registration = new RuntimeStartupRegistration(attach);
        var unregisterToken = IntPtr.Zero;
        try {
            runtimeStartupRegistration = registration;
            int result;
            unsafe {
                result = DbgShim.RegisterForRuntimeStartup(checked((uint)processId), &OnRuntimeStartup, 0, out unregisterToken);
            }
            if (result != Cor.S_OK) {
                // Whether the debuggee is still alive is what separates a launch that died from a registration that was refused
                throw new InvalidOperationException(
                    $"RegisterForRuntimeStartup failed for process {processId}: 0x{result:X8}, debuggee alive: {IsProcessAlive(processId)}",
                    Marshal.GetExceptionForHR(result));
            }

            using var cancellation = cancellationToken.Register(() => registration.Completion.TrySetCanceled(cancellationToken));
            await registration.Completion.Task.ConfigureAwait(false);
        }
        finally {
            if (unregisterToken != IntPtr.Zero)
                _ = DbgShim.UnregisterForRuntimeStartup(unregisterToken);
            runtimeStartupRegistration = null;
        }
    }

    // Builds an ICorDebug for a remote CoreCLR target (mobile/maccatalyst). There is no runtime-startup callback here:
    // the returned ICorDebug already has its remote transport set up (listening or connecting per 'RemoteAttachInfo.IsServer'),
    // and the target's ICorDebugProcess is delivered later through the CreateProcess managed callback once the on-device runtime attaches
    public static ICorDebug CreateRemote(RemoteAttachInfo attachInfo) {
        var result = DbgShim.RegisterForRuntimeStartupRemotePort(
            attachInfo.Address,
            checked((uint)attachInfo.Port),
            attachInfo.Platform,
            attachInfo.IsServer,
            attachInfo.MscordbiPath,
            attachInfo.AssembliesPath,
            out var corDebug);
        Marshal.ThrowExceptionForHR(result);

        if (corDebug == null)
            throw new InvalidOperationException("RegisterForRuntimeStartupRemotePort returned a null ICorDebug");
        return corDebug;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnRuntimeStartup(void* pCorDebug, void* parameter, int hresult) {
        // Nothing may escape an UnmanagedCallersOnly method - a throw here takes the adapter down - so every
        // outcome, including a failed attach, travels back to 'AttachAsync' through the completion
        var registration = runtimeStartupRegistration;
        if (registration == null)
            return;
        try {
            var corDebug = ComInterfaceMarshaller<ICorDebug>.ConvertToManaged(pCorDebug);
            if (corDebug == null || hresult != Cor.S_OK)
                throw new InvalidOperationException($"The runtime startup registration failed: 0x{hresult:X8}", Marshal.GetExceptionForHR(hresult));
            registration.Attach.Invoke(corDebug);
            registration.Completion.TrySetResult();
        }
        catch (Exception ex) {
            // A completion cancelled meanwhile takes neither, and nothing may throw out of here
            registration.Completion.TrySetException(ex);
        }
    }
    private static bool IsProcessAlive(int processId) {
        try {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException) {
            return false;
        }
    }

    private class RuntimeStartupRegistration {
        public Action<ICorDebug> Attach { get; }
        public TaskCompletionSource Completion { get; }

        public RuntimeStartupRegistration(Action<ICorDebug> attach) {
            Attach = attach;
            Completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
