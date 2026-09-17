#pragma once
// The little COM the agent needs, done by hand: an object is a pointer to its vtable, an array of function pointers
// whose first three are IUnknown's QueryInterface, AddRef and Release. The types are the runtime's (pal.h): HRESULT is
// a 32-bit result code, negative when the call failed; WCHAR is 16 bits on every platform, unlike C++'s wchar_t
// https://learn.microsoft.com/windows/win32/api/unknwn/nn-unknwn-iunknown
// https://learn.microsoft.com/windows/win32/com/structure-of-com-error-codes
// https://github.com/dotnet/runtime/blob/main/src/coreclr/pal/inc/pal.h
#include <cstddef>
#include <cstdint>

typedef int32_t HRESULT;
typedef uint32_t ULONG;
typedef uint32_t DWORD;
typedef int32_t BOOL;
typedef char16_t WCHAR;
// One machine word: an integer, a pointer or a handle, the way a native call takes every argument
typedef uintptr_t Word;

struct GUID {
    uint32_t Data1;
    uint16_t Data2;
    uint16_t Data3;
    uint8_t Data4[8];
};

// https://learn.microsoft.com/windows/win32/seccrypto/common-hresult-values
const HRESULT S_OK = 0;
const HRESULT S_FALSE = 1;
const HRESULT E_NOTIMPL = (HRESULT)0x80004001;
const HRESULT E_NOINTERFACE = (HRESULT)0x80004002;
const HRESULT E_POINTER = (HRESULT)0x80004003;
const HRESULT E_FAIL = (HRESULT)0x80004005;
const HRESULT E_HANDLE = (HRESULT)0x80070006;
const HRESULT E_INVALIDARG = (HRESULT)0x80070057;

// The interface ids, from cordebug.idl and corprof.idl in dotnet/runtime (src/coreclr/inc) and the COM headers
// https://github.com/dotnet/runtime/blob/main/src/coreclr/inc/cordebug.idl
// https://github.com/dotnet/runtime/blob/main/src/coreclr/inc/corprof.idl
// https://learn.microsoft.com/windows/win32/api/unknwnbase/nn-unknwnbase-iclassfactory
extern const GUID IID_IUnknown;
extern const GUID IID_IClassFactory;
extern const GUID IID_ICorProfilerCallback;
extern const GUID IID_ICorProfilerCallback2;
extern const GUID IID_ICorDebugManagedCallback;
extern const GUID IID_ICorDebugManagedCallback2;

// A function of a vtable; the real parameter list is whatever the slot's method takes
typedef HRESULT (*Method)(void* self, ...);
struct ComObject {
    const Method* vtable;
};

bool SameGuid(const GUID* a, const GUID* b);
// The object's interface 'iid', with a reference the caller releases; NULL when the object has no such interface
// https://learn.microsoft.com/windows/win32/com/rules-for-implementing-queryinterface
void* QueryInterface(void* object, const GUID* iid);
// https://learn.microsoft.com/windows/win32/com/managing-object-lifetimes-through-reference-counting
void AddRef(void* object);
void Release(void* object);

// Calls the method in 'slot' of 'object' with the given words, one per parameter. On the platforms the agent runs on
// (the arm64 and x86-64 calling conventions of Apple and Android) a method ignores words beyond its parameters, since
// the caller owns the argument registers and the stack it pushed, so one call shape serves every method, up to MaxWords
// https://developer.apple.com/documentation/xcode/writing-arm64-code-for-apple-platforms
const int MaxWords = 20;
HRESULT CallMethod(void* object, int slot, const Word* words, int count);
HRESULT CallMethod(void* object, int slot);
HRESULT CallMethod(void* object, int slot, Word first);
HRESULT CallMethod(void* object, int slot, Word first, Word second, Word third);

// Marks a function the library exports by name; everything else is hidden by the build's -fvisibility=hidden
// https://gcc.gnu.org/wiki/Visibility
#define EXPORT extern "C" __attribute__((visibility("default")))
