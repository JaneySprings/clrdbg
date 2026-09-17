#pragma once
#include "Com.h"

// The objects the host holds, one handle per object. COM identity is the IUnknown pointer (the QueryInterface rules),
// so the same object always gets the same handle and the host keeps one proxy per object.
// https://learn.microsoft.com/windows/win32/com/rules-for-implementing-queryinterface The table holds one reference per object; every
// hand-out of the handle counts, and the host gives the counts back with Release requests, or all at once when it
// disconnects
// Hands out the handle of 'object', 0 for NULL. 'ownsReference' says the caller holds a reference to give away (an out
// parameter of a call); a callback argument is only borrowed, so the table takes a reference of its own
uint32_t RegisterObject(void* object, bool ownsReference);
// The interface 'iid' of the object behind 'handle', with a reference the caller releases
HRESULT ResolveHandle(uint32_t handle, const GUID* iid, void** result);
void ReleaseHandle(uint32_t handle, uint32_t count);
void ReleaseAllHandles();
