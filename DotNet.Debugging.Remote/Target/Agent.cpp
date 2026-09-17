// The in-process debugging agent for CoreCLR on Apple platforms and Android (docs/remote): loaded through the runtime's
// profiler hook (Profiler.cpp)
// only because that is the supported way to get a native library into the process before managed code runs, it
// creates an ICorDebug through the app's own libmscordbi, attaches it to the runtime of this very process and serves
// a host debugger over TCP. The host drives the ICorDebug object model remotely: every object the debugger hands out
// gets a handle (Debugger/Handles), a request names a handle, an interface, a vtable slot and the arguments and the
// agent makes the call (Debugger/Invoke), the debugger's callbacks go out as events carrying the handles of their
// arguments (Debugger/Callbacks). The launcher tells the agent where the debugger is through the environment:
// CORECLR_REMOTE_DEBUGGER_IP and _PORT name the debugger, CORECLR_REMOTE_DEBUGGER_ISSERVER says whether the app
// listens ("1") or connects out ("0"). Written from the dotnet/runtime headers and sources and the ICorDebug and
// ICorProfiler documentation. The C++ is kept plain: functions, structs, std::string and std::vector (string and
// List), std::mutex with a lock_guard (the lock block), no templates of our own
#include <cstdlib>
#include <cstring>
#include <dlfcn.h>
#include <libgen.h>
#include <pthread.h>
#include <string>
#include <unistd.h>
#include <vector>
#include "Agent.h"
#include "Debugger/Callbacks.h"
#include "Debugger/Requests.h"
#include "Log.h"
#include "Protocol/Connection.h"

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebug-interface
static const int ICorDebug_Initialize = 3;
static const int ICorDebug_SetManagedHandler = 5;
static const int ICorDebug_DebugActiveProcess = 8;
// The ICorDebug version asked of mscordbi (CorDebugInterfaceVersion in cordebug.idl)
// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebuginterfaceversion-enumeration
static const int CorDebugVersion_2_0 = 3;
static const DWORD DLL_PROCESS_ATTACH = 1;
// How long the attach waits for the host before it goes ahead without one
static const int HostTimeoutMilliseconds = 30000;

// The two exports of libmscordbi the agent calls (src/coreclr/debug/di/cordb.cpp): DllMain, where the library stands up
// the PAL it shares with the DAC (PAL_InitializeDLL), and CoreCLRCreateCordbObject3, which makes the ICorDebug
// (Cordb::CreateObject) and tells it the runtime it debugs (SetTargetCLR)
// https://github.com/dotnet/runtime/blob/main/src/coreclr/debug/di/cordb.cpp
typedef BOOL (*DllMainFunction)(void* instance, DWORD reason, void* reserved);
typedef HRESULT (*CreateCordbObject3Function)(int version, DWORD pid, const WCHAR* applicationGroupId, const WCHAR* dacModulePath, void* targetClr, void** cordb);

// The file name of a shared library on this platform
#ifdef __APPLE__
static const char* LibraryExtension = ".dylib";
#else
static const char* LibraryExtension = ".so";
#endif

// A library of the runtime pack as the platforms lay it out: flat next to libcoreclr (Mac Catalyst, the simulators,
// Android's lib folder in the APK), or in a framework of its own next to libcoreclr.framework (the iOS device:
// Frameworks/libmscordbi.framework/libmscordbi); empty when it is in neither place
static std::string FindRuntimeLibrary(const std::string& runtimeDirectory, const char* name) {
    std::string flat = runtimeDirectory + "/lib" + name + LibraryExtension;
    if (access(flat.c_str(), F_OK) == 0)
        return flat;
    std::string frameworks = runtimeDirectory;
    size_t slash = frameworks.rfind('/');
    if (slash != std::string::npos)
        frameworks = frameworks.substr(0, slash);
    std::string framework = frameworks + "/lib" + name + ".framework/lib" + name;
    if (access(framework.c_str(), F_OK) == 0)
        return framework;
    return "";
}
// The runtime's UTF-16 (NUL-terminated) for a UTF-8 path
static std::vector<WCHAR> ToWide(const std::string& text) {
    std::vector<WCHAR> wide;
    size_t i = 0;
    while (i < text.size()) {
        unsigned char lead = (unsigned char)text[i];
        uint32_t code;
        int continuation;
        if (lead < 0x80) {
            code = lead;
            continuation = 0;
        }
        else if ((lead & 0xE0) == 0xC0) {
            code = lead & 0x1F;
            continuation = 1;
        }
        else if ((lead & 0xF0) == 0xE0) {
            code = lead & 0x0F;
            continuation = 2;
        }
        else {
            code = lead & 0x07;
            continuation = 3;
        }
        i++;
        for (int k = 0; k < continuation && i < text.size(); k++, i++)
            code = (code << 6) | ((unsigned char)text[i] & 0x3F);
        if (code >= 0x10000) {
            code -= 0x10000;
            wide.push_back((WCHAR)(0xD800 + (code >> 10)));
            wide.push_back((WCHAR)(0xDC00 + (code & 0x3FF)));
        }
        else {
            wide.push_back((WCHAR)code);
        }
    }
    wide.push_back(0);
    return wide;
}

// The ICorDebug of this process, made by the app's own mscordbi; NULL when that failed
static void* CreateDebugger() {
    // The app's own runtime folder holds libmscordbi and libmscordaccore, next to libcoreclr, found by the address of
    // the hosting API's coreclr_initialize (src/coreclr/hosts/inc/coreclrhost.h). The global lookup sees the runtime
    // when its loader made it global (Apple); Android's loader keeps a dlopen'ed library private, so the loaded
    // library is asked for by name, without loading it again
    // https://github.com/dotnet/runtime/blob/main/src/coreclr/hosts/inc/coreclrhost.h
    Dl_info coreclr;
    void* coreclrSymbol = dlsym(RTLD_DEFAULT, "coreclr_initialize");
    if (coreclrSymbol == NULL) {
        std::string libraryName = std::string("libcoreclr") + LibraryExtension;
        void* loaded = dlopen(libraryName.c_str(), RTLD_NOLOAD | RTLD_NOW);
        if (loaded != NULL)
            coreclrSymbol = dlsym(loaded, "coreclr_initialize");
    }
    if (coreclrSymbol == NULL || dladdr(coreclrSymbol, &coreclr) == 0) {
        Log("libcoreclr not found in the process");
        return NULL;
    }
    std::string runtimePath = coreclr.dli_fname;
    std::string runtimeDirectory = dirname(&runtimePath[0]);
    SetRuntimeDirectory(runtimeDirectory);
    Log("runtime at %s (base %p)", runtimeDirectory.c_str(), coreclr.dli_fbase);

    std::string dbiPath = FindRuntimeLibrary(runtimeDirectory, "mscordbi");
    std::string dacPath = FindRuntimeLibrary(runtimeDirectory, "mscordaccore");
    if (dbiPath.empty()) {
        Log("libmscordbi not found next to %s, neither as libmscordbi%s nor as libmscordbi.framework/libmscordbi", runtimeDirectory.c_str(), LibraryExtension);
        return NULL;
    }
    Log("mscordbi at %s, DAC at %s", dbiPath.c_str(), dacPath.empty() ? "(not found, mscordbi looks for it itself)" : dacPath.c_str());
    void* dbi = dlopen(dbiPath.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (dbi == NULL) {
        Log("dlopen(%s) failed: %s", dbiPath.c_str(), dlerror());
        return NULL;
    }
    // The runtime's own loader (the PAL's LoadLibrary, LOADRegisterLibraryDirect in src/coreclr/pal/src/loader/module.cpp)
    // runs a module's DllMain with DLL_PROCESS_ATTACH right after dlopen, and that is where mscordbi stands up the PAL
    // it shares with the DAC; a plain dlopen skips it, and the first PAL call would then crash
    // https://github.com/dotnet/runtime/blob/main/src/coreclr/pal/src/loader/module.cpp
    DllMainFunction dllMain = (DllMainFunction)dlsym(dbi, "DllMain");
    if (dllMain == NULL) {
        Log("DllMain not exported by %s", dbiPath.c_str());
        return NULL;
    }
    BOOL attached = dllMain(NULL, DLL_PROCESS_ATTACH, NULL);
    Log("mscordbi DllMain(DLL_PROCESS_ATTACH): %d", (int)attached);

    CreateCordbObject3Function create = (CreateCordbObject3Function)dlsym(dbi, "CoreCLRCreateCordbObject3");
    if (create == NULL) {
        Log("CoreCLRCreateCordbObject3 not exported by %s", dbiPath.c_str());
        return NULL;
    }
    // mscordbi needs the identity of the runtime it debugs: the base of libcoreclr in this process. It loads the DAC
    // from its own folder unless told where it is (ShimProcess::GetDacModule, src/coreclr/debug/di/shimprocess.cpp);
    // in the framework layout that folder holds mscordbi alone, so the DAC's path goes along whenever it was found
    // https://github.com/dotnet/runtime/blob/main/src/coreclr/debug/di/shimprocess.cpp
    std::vector<WCHAR> dacPathWide = ToWide(dacPath);
    void* cordb = NULL;
    HRESULT hr = create(CorDebugVersion_2_0, (DWORD)getpid(), NULL, dacPath.empty() ? NULL : dacPathWide.data(), coreclr.dli_fbase, &cordb);
    Log("CoreCLRCreateCordbObject3: 0x%08x", (unsigned)hr);
    if (hr != S_OK || cordb == NULL)
        return NULL;
    // https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebug-initialize-method
    hr = CallMethod(cordb, ICorDebug_Initialize);
    Log("ICorDebug::Initialize: 0x%08x", (unsigned)hr);
    // https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebug-setmanagedhandler-method
    hr = CallMethod(cordb, ICorDebug_SetManagedHandler, (Word)GetManagedCallback());
    Log("ICorDebug::SetManagedHandler: 0x%08x", (unsigned)hr);
    return cordb;
}

static void* AgentMain(void* argument) {
    Log("agent thread running");
    void* cordb = CreateDebugger();
    if (cordb == NULL)
        return NULL;

    // The host is reached before the attach, so it sees the callbacks from the first one: the app listens when it is
    // the server, else it connects out to the debugger. A host that cannot be reached in time gets the attach done
    // without it, and a listening app still takes a host later
    const char* portText = getenv("CORECLR_REMOTE_DEBUGGER_PORT");
    const char* addressText = getenv("CORECLR_REMOTE_DEBUGGER_IP");
    const char* isServerText = getenv("CORECLR_REMOTE_DEBUGGER_ISSERVER");
    int port = portText != NULL ? atoi(portText) : 0;
    bool appIsServer = isServerText != NULL && strcmp(isServerText, "1") == 0;
    int listener = -1;
    int connection = -1;
    if (port <= 0) {
        Log("CORECLR_REMOTE_DEBUGGER_PORT not set, the agent runs without a host");
    }
    else if (appIsServer) {
        listener = Listen(port, addressText);
        if (listener >= 0) {
            connection = AcceptHost(listener, HostTimeoutMilliseconds);
            if (connection < 0)
                Log("no host connected within %d ms, attaching without one", HostTimeoutMilliseconds);
        }
    }
    else {
        connection = ConnectToHost(addressText != NULL ? addressText : "127.0.0.1", port, HostTimeoutMilliseconds);
    }

    // Attaches to this very process: the callbacks start with CreateProcess and go on with the app domain, the
    // assemblies and modules loaded so far, and the threads. A host that connects later (the first one is gone, or
    // none came in time) learns from Hello that it missed them and replays the attach from the process
    // https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebug-debugactiveprocess-method
    void* process = NULL;
    HRESULT hr = CallMethod(cordb, ICorDebug_DebugActiveProcess, (Word)getpid(), (Word)0, (Word)&process);
    Log("ICorDebug::DebugActiveProcess: 0x%08x", (unsigned)hr);
    if (connection >= 0)
        ServeHostUntilGone(connection);
    while (listener >= 0) {
        connection = AcceptHost(listener, -1);
        if (connection >= 0)
            ServeHostUntilGone(connection);
    }
    return NULL;
}

void StartAgentThread() {
    pthread_t thread;
    pthread_attr_t attributes;
    pthread_attr_init(&attributes);
    pthread_attr_setdetachstate(&attributes, PTHREAD_CREATE_DETACHED);
    int result = pthread_create(&thread, &attributes, AgentMain, NULL);
    pthread_attr_destroy(&attributes);
    if (result != 0)
        Log("pthread_create failed: %d", result);
}
