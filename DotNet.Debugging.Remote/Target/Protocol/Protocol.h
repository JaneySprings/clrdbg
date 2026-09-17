#pragma once
// The wire protocol between the agent and the host (docs/remote/protocol.md), little-endian:
// frame:    uint32 length of what follows, uint8 kind, uint32 sequence, then the body
// request:  uint16 command, arguments             (host -> agent)
// response: int32 hresult, result                 (agent -> host, the request's sequence)
// event:    uint16 event, data                    (agent -> host, sequence 0)
#include <cstdint>

const uint32_t ProtocolVersion = 4;

const uint8_t FrameKind_Request = 1;
const uint8_t FrameKind_Response = 2;
const uint8_t FrameKind_Event = 3;

const uint16_t Command_Hello = 1;
const uint16_t Command_Invoke = 2;
const uint16_t Command_Release = 3;
const uint16_t Command_Terminate = 4;
const uint16_t Command_Query = 5;

const uint16_t Event_Callback = 1;

// The argument kinds of an Invoke request, each expanded into the words of the native call
const uint8_t Arg_UInt32 = 1;
const uint8_t Arg_UInt64 = 2;
const uint8_t Arg_Object = 3;
const uint8_t Arg_OutUInt32 = 4;
const uint8_t Arg_OutUInt64 = 5;
const uint8_t Arg_OutObject = 6;
const uint8_t Arg_OutString = 7;
const uint8_t Arg_Bytes = 8;
const uint8_t Arg_OutBytes = 9;
const uint8_t Arg_Objects = 10;
const uint8_t Arg_OutObjects = 11;
const uint8_t Arg_Guid = 12;
const uint8_t Arg_String = 13;
const uint8_t Arg_OutStringBuffer = 14;
const uint8_t Arg_RefUInt64 = 15;
const uint8_t Arg_OutBlob = 16;
const uint8_t Arg_OutGuid = 17;
const uint8_t Arg_OutRecords = 18;

// The callbacks, numbered in the order of ICorDebugManagedCallback and ICorDebugManagedCallback2
enum CallbackId {
    CallbackId_Breakpoint = 1,
    CallbackId_StepComplete = 2,
    CallbackId_Break = 3,
    CallbackId_Exception = 4,
    CallbackId_EvalComplete = 5,
    CallbackId_EvalException = 6,
    CallbackId_CreateProcess = 7,
    CallbackId_ExitProcess = 8,
    CallbackId_CreateThread = 9,
    CallbackId_ExitThread = 10,
    CallbackId_LoadModule = 11,
    CallbackId_UnloadModule = 12,
    CallbackId_LoadClass = 13,
    CallbackId_UnloadClass = 14,
    CallbackId_DebuggerError = 15,
    CallbackId_LogMessage = 16,
    CallbackId_LogSwitch = 17,
    CallbackId_CreateAppDomain = 18,
    CallbackId_ExitAppDomain = 19,
    CallbackId_LoadAssembly = 20,
    CallbackId_UnloadAssembly = 21,
    CallbackId_ControlCTrap = 22,
    CallbackId_NameChange = 23,
    CallbackId_UpdateModuleSymbols = 24,
    CallbackId_EditAndContinueRemap = 25,
    CallbackId_BreakpointSetError = 26,
    CallbackId_FunctionRemapOpportunity = 27,
    CallbackId_CreateConnection = 28,
    CallbackId_ChangeConnection = 29,
    CallbackId_DestroyConnection = 30,
    CallbackId_Exception2 = 31,
    CallbackId_ExceptionUnwind = 32,
    CallbackId_FunctionRemapComplete = 33,
    CallbackId_MDANotification = 34,
};
