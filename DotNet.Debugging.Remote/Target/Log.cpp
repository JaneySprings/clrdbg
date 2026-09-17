#ifdef CLRDBG_TRACE
#include <cstdarg>
#include <cstdio>
#include <mutex>
#include "Log.h"
#ifdef __ANDROID__
#include <android/log.h>
#endif

static std::mutex logLock;

// Like printf, one line per call, prefixed with the library's name so the app's own output stays apart
void Log(const char* format, ...) {
    std::lock_guard<std::mutex> guard(logLock);
    va_list arguments;
    va_start(arguments, format);
#ifdef __ANDROID__
    // https://developer.android.com/ndk/reference/group/logging
    __android_log_vprint(ANDROID_LOG_INFO, "remotecoreclrtarget", format, arguments);
#else
    printf("[remotecoreclrtarget] ");
    vprintf(format, arguments);
    printf("\n");
    fflush(stdout);
#endif
    va_end(arguments);
}
#endif
