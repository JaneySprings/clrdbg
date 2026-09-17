#pragma once
#include "Protocol/ByteReader.h"
#include "Protocol/ByteWriter.h"

// The Invoke request: a call on a handle's interface, argument by argument. Every argument kind expands into the words
// of the native call (a value, a pointer to storage, or the three words of a string pattern); what the callee wrote
// comes back in the response in the same order, zeroed when the call failed
// Invoke: uint32 handle, GUID interface, uint16 slot, uint8 argument count, the arguments; the result is the callee's
// HRESULT followed by the out values
HRESULT HandleInvoke(ByteReader& arguments, ByteWriter& result);
// Query: uint32 handle count, uint8 interface count, the handles, the interfaces; the result is one byte per handle and
// interface, in that order, 1 when the object behind the handle has the interface. That is how the host tells the kind
// of a value or a frame apart before it makes the proxy
HRESULT HandleQuery(ByteReader& arguments, ByteWriter& result);
