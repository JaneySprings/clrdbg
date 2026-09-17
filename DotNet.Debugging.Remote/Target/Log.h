#pragma once
// Trace output of the agent: the app's standard output on Apple platforms, where a debugger console shows it, logcat on
// Android (tag 'remotecoreclrtarget'). Built in only with the build scripts' --enable-trace (the CLRDBG_TRACE define);
// without it every Log call compiles to nothing
#ifdef CLRDBG_TRACE
void Log(const char* format, ...);
#else
#define Log(...) ((void)0)
#endif
