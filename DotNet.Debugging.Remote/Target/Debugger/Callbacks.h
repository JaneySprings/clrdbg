#pragma once
#include "Com.h"

// The debugger's callbacks (ICorDebugManagedCallback and ICorDebugManagedCallback2). Every callback is reported to the
// host as an event carrying the handles of its arguments and left stopped: the host continues the controller when it
// is done with the stop, the way a debugger continues a queued callback. Without a host the process is continued right
// away. mscordbi raises them on its own event thread (CordbRCEventThread in src/coreclr/debug/di/process.cpp), and the
// process stays stopped after a callback returns until someone calls Continue
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-interface
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback2-interface
// https://github.com/dotnet/runtime/blob/main/src/coreclr/debug/di/process.cpp
// The object to hand ICorDebug::SetManagedHandler
void* GetManagedCallback();
// The ICorDebugProcess the CreateProcess callback brought, NULL before that
void* GetDebuggedProcess();
// A handle of the process for a host that missed the attach (it connected after the CreateProcess callback went to an
// earlier host, or to no host at all), so it can replay the attach from the process itself; 0 for the host the attach
// was reported to, and before the process exists
uint32_t GetProcessHandleForReplay();
// Lets the controller run; a NULL controller (a thread's NameChange has a null app domain) means the process
void ContinueController(void* controller, const char* name);
