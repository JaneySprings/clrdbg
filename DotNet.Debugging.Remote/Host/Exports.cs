using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote;

// The one export dbgshim looks for in a remote debugging host library: 'CreateRemoteCordbObject', called by
// 'RegisterForRuntimeStartupRemotePort' with what the launch configured. The strings are the runtime's 16-bit WCHARs.
// The object goes out through the same marshaller the generated interfaces use, so the proxies it hands out later and
// the ones the debugger passes back are one and the same
public static unsafe class Exports {
    private static readonly List<RemoteCorDebug> debuggers = new List<RemoteCorDebug>();

    [UnmanagedCallersOnly(EntryPoint = "CreateRemoteCordbObject")]
    public static int CreateRemoteCordbObject(char* address, uint port, char* platform, int isServer, char* assembliesPath, void** result) {
        try {
            if (result == null)
                return Cor.E_POINTER;
            var debugger = new RemoteCorDebug(ReadString(address) ?? "127.0.0.1", (int)port, isServer != 0, ReadString(platform), ReadString(assembliesPath));
            lock (debuggers)
                debuggers.Add(debugger);
            *result = ComInterfaceMarshaller<ICorDebug>.ConvertToUnmanaged(debugger);
            HostLog.Write($"CreateRemoteCordbObject: {debugger}");
            return Cor.S_OK;
        }
        catch (Exception ex) {
            HostLog.Write($"CreateRemoteCordbObject failed: {ex}");
            return Cor.E_FAIL;
        }
    }

    private static string? ReadString(char* text) {
        return text == null ? null : new string(text);
    }
}
