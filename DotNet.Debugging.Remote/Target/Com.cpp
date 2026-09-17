#include <cstring>
#include "Com.h"

const GUID IID_IUnknown = { 0x00000000, 0x0000, 0x0000, { 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 } };
const GUID IID_IClassFactory = { 0x00000001, 0x0000, 0x0000, { 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 } };
const GUID IID_ICorProfilerCallback = { 0x176FBED1, 0xA55C, 0x4796, { 0x98, 0xCA, 0xA9, 0xDA, 0x0E, 0xF8, 0x83, 0xE7 } };
const GUID IID_ICorProfilerCallback2 = { 0x8A8CC829, 0xCCF2, 0x49fe, { 0xBB, 0xAE, 0x0F, 0x02, 0x22, 0x28, 0x07, 0x1A } };
const GUID IID_ICorDebugManagedCallback = { 0x3d6f5f60, 0x7538, 0x11d3, { 0x8d, 0x5b, 0x00, 0x10, 0x4b, 0x35, 0xe7, 0xef } };
const GUID IID_ICorDebugManagedCallback2 = { 0x250E5EEA, 0xDB5C, 0x4C76, { 0xB6, 0xF3, 0x8C, 0x46, 0xF1, 0x2E, 0x32, 0x03 } };

typedef HRESULT (*QueryInterfaceMethod)(void* self, const GUID* iid, void** result);
typedef ULONG (*CountMethod)(void* self);
typedef HRESULT (*AnyMethod)(void* self,
    Word, Word, Word, Word, Word, Word, Word, Word, Word, Word,
    Word, Word, Word, Word, Word, Word, Word, Word, Word, Word);

bool SameGuid(const GUID* a, const GUID* b) {
    return memcmp(a, b, sizeof(GUID)) == 0;
}
void* QueryInterface(void* object, const GUID* iid) {
    ComObject* com = (ComObject*)object;
    QueryInterfaceMethod method = (QueryInterfaceMethod)com->vtable[0];
    void* result = NULL;
    if (method(object, iid, &result) != S_OK)
        return NULL;
    return result;
}
void AddRef(void* object) {
    ComObject* com = (ComObject*)object;
    CountMethod method = (CountMethod)com->vtable[1];
    method(object);
}
void Release(void* object) {
    ComObject* com = (ComObject*)object;
    CountMethod method = (CountMethod)com->vtable[2];
    method(object);
}

HRESULT CallMethod(void* object, int slot, const Word* words, int count) {
    if (count > MaxWords)
        return E_INVALIDARG;
    Word w[MaxWords];
    for (int i = 0; i < MaxWords; i++)
        w[i] = i < count ? words[i] : 0;
    ComObject* com = (ComObject*)object;
    AnyMethod method = (AnyMethod)com->vtable[slot];
    return method(object, w[0], w[1], w[2], w[3], w[4], w[5], w[6], w[7], w[8], w[9],
        w[10], w[11], w[12], w[13], w[14], w[15], w[16], w[17], w[18], w[19]);
}
HRESULT CallMethod(void* object, int slot) {
    return CallMethod(object, slot, NULL, 0);
}
HRESULT CallMethod(void* object, int slot, Word first) {
    Word words[1] = { first };
    return CallMethod(object, slot, words, 1);
}
HRESULT CallMethod(void* object, int slot, Word first, Word second, Word third) {
    Word words[3] = { first, second, third };
    return CallMethod(object, slot, words, 3);
}
