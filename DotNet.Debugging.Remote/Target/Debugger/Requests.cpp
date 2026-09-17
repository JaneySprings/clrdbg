#include <cstdlib>
#include <unistd.h>
#include <vector>
#include "Debugger/Callbacks.h"
#include "Debugger/Handles.h"
#include "Debugger/Invoke.h"
#include "Debugger/Requests.h"
#include "Log.h"
#include "Protocol/ByteReader.h"
#include "Protocol/Connection.h"
#include "Protocol/Protocol.h"

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcontroller-interface
static const int ICorDebugController_Continue = 4;
static const int ICorDebugController_IsRunning = 5;
static const int ICorDebugController_Terminate = 10;
static std::string runtimeDirectory;

void SetRuntimeDirectory(const std::string& directory) {
    runtimeDirectory = directory;
}

// Terminate is answered before it is carried out: the process, this one, is gone right after. The debugger ends it the
// way a host debugger would, and the process ends itself should the debugger refuse
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcontroller-terminate-method
static void HandleTerminate(uint32_t sequence, ByteReader& arguments) {
    uint32_t exitCode = arguments.Has(4) ? arguments.ReadUInt32() : 0;
    Log("request Terminate (seq %u): exiting with %u", (unsigned)sequence, (unsigned)exitCode);
    ByteWriter nothing;
    SendResponse(sequence, S_OK, nothing);
    void* process = GetDebuggedProcess();
    if (process == NULL || CallMethod(process, ICorDebugController_Terminate, (Word)exitCode) != S_OK)
        _exit((int)exitCode);
}
static void HandleRequest(uint32_t sequence, uint16_t command, ByteReader& arguments) {
    if (command == Command_Terminate) {
        HandleTerminate(sequence, arguments);
        return;
    }
    ByteWriter result;
    HRESULT hr = S_OK;
    switch (command) {
        case Command_Hello: {
            uint32_t replay = GetProcessHandleForReplay();
            result.WriteUInt32(ProtocolVersion);
            result.WriteUInt32((uint32_t)getpid());
            result.WriteString(runtimeDirectory);
            result.WriteUInt32(replay);
            Log("request Hello (seq %u)%s", (unsigned)sequence, replay != 0 ? ", the host replays the attach" : "");
            break;
        }
        case Command_Invoke:
            hr = HandleInvoke(arguments, result);
            break;
        // Any number of (handle, count) pairs: the host gathers the hand-outs it gives back
        case Command_Release:
            while (arguments.Has(8)) {
                uint32_t handle = arguments.ReadUInt32();
                uint32_t count = arguments.ReadUInt32();
                ReleaseHandle(handle, count);
            }
            break;
        case Command_Query:
            hr = HandleQuery(arguments, result);
            break;
        default:
            hr = E_NOTIMPL;
            Log("request %u (seq %u) is unknown", (unsigned)command, (unsigned)sequence);
            break;
    }
    SendResponse(sequence, hr, result);
}
// A host that goes away leaves nothing stopped behind: the process is continued until it runs (each callback the
// host did not continue holds one stop, and every Stop the host asked for another)
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcontroller-isrunning-method
static void ReleaseStopsHeldForHost() {
    void* process = GetDebuggedProcess();
    if (process == NULL)
        return;
    for (int i = 0; i < 8; i++) {
        BOOL running = 0;
        if (CallMethod(process, ICorDebugController_IsRunning, (Word)&running) != S_OK || running != 0)
            return;
        if (CallMethod(process, ICorDebugController_Continue, (Word)0) != S_OK)
            return;
    }
}

void ServeHostUntilGone(int connection) {
    std::vector<uint8_t> frame;
    while (ReadFrame(connection, frame)) {
        ByteReader reader(frame.data(), frame.size());
        uint8_t kind = reader.ReadByte();
        uint32_t sequence = reader.ReadUInt32();
        if (kind != FrameKind_Request || !reader.Has(2)) {
            Log("unexpected frame kind %u, dropping the host", (unsigned)kind);
            break;
        }
        uint16_t command = reader.ReadUInt16();
        HandleRequest(sequence, command, reader);
    }
    CloseHost(connection);
    ReleaseStopsHeldForHost();
    ReleaseAllHandles();
}
