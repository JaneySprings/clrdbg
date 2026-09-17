#include <mutex>
#include <unordered_map>
#include "Debugger/Handles.h"
#include "Log.h"

struct HandleEntry {
    void* object;
    uint32_t issued;
};
static std::mutex handleLock;
static std::unordered_map<uint32_t, HandleEntry> handles;
static std::unordered_map<void*, uint32_t> handlesByIdentity;
static uint32_t nextHandle = 1;

static void* IdentityOf(void* object) {
    void* unknown = QueryInterface(object, &IID_IUnknown);
    if (unknown == NULL)
        return object;
    Release(unknown);
    return unknown;
}

uint32_t RegisterObject(void* object, bool ownsReference) {
    if (object == NULL)
        return 0;
    void* identity = IdentityOf(object);
    void* duplicate = NULL;
    uint32_t handle;
    {
        std::lock_guard<std::mutex> guard(handleLock);
        std::unordered_map<void*, uint32_t>::iterator found = handlesByIdentity.find(identity);
        if (found != handlesByIdentity.end()) {
            handle = found->second;
            handles[handle].issued++;
            if (ownsReference)
                duplicate = object;
        }
        else {
            if (!ownsReference)
                AddRef(object);
            handle = nextHandle;
            nextHandle++;
            HandleEntry entry;
            entry.object = object;
            entry.issued = 1;
            handles[handle] = entry;
            handlesByIdentity[identity] = handle;
        }
    }
    // The table already held a reference to this object: the one given away is not needed
    if (duplicate != NULL)
        Release(duplicate);
    return handle;
}
HRESULT ResolveHandle(uint32_t handle, const GUID* iid, void** result) {
    void* object;
    {
        std::lock_guard<std::mutex> guard(handleLock);
        std::unordered_map<uint32_t, HandleEntry>::iterator found = handles.find(handle);
        if (found == handles.end())
            return E_HANDLE;
        object = found->second.object;
        AddRef(object);
    }
    *result = QueryInterface(object, iid);
    Release(object);
    return *result != NULL ? S_OK : E_NOINTERFACE;
}
void ReleaseHandle(uint32_t handle, uint32_t count) {
    void* released = NULL;
    {
        std::lock_guard<std::mutex> guard(handleLock);
        std::unordered_map<uint32_t, HandleEntry>::iterator found = handles.find(handle);
        if (found == handles.end())
            return;
        if (count < found->second.issued)
            found->second.issued -= count;
        else
            found->second.issued = 0;
        if (found->second.issued > 0)
            return;
        released = found->second.object;
        handlesByIdentity.erase(IdentityOf(released));
        handles.erase(found);
    }
    Release(released);
}
void ReleaseAllHandles() {
    std::unordered_map<uint32_t, HandleEntry> released;
    {
        std::lock_guard<std::mutex> guard(handleLock);
        released.swap(handles);
        handlesByIdentity.clear();
    }
    for (std::unordered_map<uint32_t, HandleEntry>::iterator entry = released.begin(); entry != released.end(); ++entry)
        Release(entry->second.object);
    if (!released.empty())
        Log("released %zu handles", released.size());
}
