#pragma once

// Starts the agent's thread: it creates an ICorDebug from the app's own mscordbi, reaches the host and attaches the
// debugger to this process (see Agent.cpp)
void StartAgentThread();
