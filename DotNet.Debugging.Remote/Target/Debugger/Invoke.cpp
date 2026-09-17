#include <cstring>
#include <vector>
#include "Debugger/Handles.h"
#include "Debugger/Invoke.h"
#include "Log.h"
#include "Protocol/Protocol.h"

// One argument of a call, as read from the request and as the callee left it
struct CallArgument {
    uint8_t kind;
    uint64_t value;              // in and out integers, the count of a blob
    void* object;                // in: the interface resolved for the call; out: what the callee returned, or a blob's pointer
    uint32_t count;              // buffer sizes and array capacities
    uint32_t length;             // OutString, OutStringBuffer: the length the callee reported
    uint32_t unit;               // OutBlob: bytes per unit of the count, and the least to copy
    uint32_t minimum;
    uint32_t recordSize;         // OutRecords: the size of one record
    std::vector<uint32_t> offsets;   // OutRecords: where a record holds an interface pointer
    std::vector<GUID> probes;    // OutObject, OutObjects: the interfaces the host asks each returned object for
    std::vector<uint8_t> bytes;
    std::vector<WCHAR> text;
    std::vector<void*> objects;
    GUID guid;

    CallArgument() {
        kind = 0;
        value = 0;
        object = NULL;
        count = 0;
        length = 0;
        unit = 0;
        minimum = 0;
        recordSize = 0;
        memset(&guid, 0, sizeof(guid));
    }
};

// The interfaces the host wants to know of a returned object, one byte per probe
static bool ReadProbes(ByteReader& reader, std::vector<GUID>& probes) {
    if (!reader.Has(1))
        return false;
    uint8_t count = reader.ReadByte();
    if (!reader.Has(sizeof(GUID) * (size_t)count))
        return false;
    for (uint8_t i = 0; i < count; i++)
        probes.push_back(reader.ReadGuid());
    return true;
}
// Reads one argument; false when the request is malformed. A handle that does not resolve sets 'failure'
static bool ReadArgument(ByteReader& reader, CallArgument& arg, HRESULT& failure) {
    if (!reader.Has(1))
        return false;
    arg.kind = reader.ReadByte();
    switch (arg.kind) {
        case Arg_UInt32:
            if (!reader.Has(4)) return false;
            arg.value = reader.ReadUInt32();
            return true;
        case Arg_UInt64:
        case Arg_RefUInt64:
            if (!reader.Has(8)) return false;
            arg.value = reader.ReadUInt64();
            return true;
        case Arg_Object: {
            if (!reader.Has(4 + sizeof(GUID))) return false;
            uint32_t handle = reader.ReadUInt32();
            GUID iid = reader.ReadGuid();
            if (handle != 0 && ResolveHandle(handle, &iid, &arg.object) != S_OK)
                failure = E_HANDLE;
            return true;
        }
        case Arg_OutUInt32:
        case Arg_OutUInt64:
        case Arg_OutGuid:
            return true;
        case Arg_OutObject:
            return ReadProbes(reader, arg.probes);
        case Arg_OutString:
        case Arg_OutStringBuffer:
            if (!reader.Has(4)) return false;
            arg.count = reader.ReadUInt32();
            arg.text.assign(arg.count, 0);
            return true;
        case Arg_Bytes:
            if (!reader.Has(4)) return false;
            arg.count = reader.ReadUInt32();
            if (!reader.Has(arg.count)) return false;
            arg.bytes.resize(arg.count);
            reader.ReadBytes(arg.bytes.data(), arg.count);
            return true;
        case Arg_OutBytes:
            if (!reader.Has(4)) return false;
            arg.count = reader.ReadUInt32();
            arg.bytes.assign(arg.count, 0);
            return true;
        case Arg_Objects: {
            if (!reader.Has(4 + sizeof(GUID))) return false;
            arg.count = reader.ReadUInt32();
            GUID iid = reader.ReadGuid();
            if (!reader.Has(4 * (size_t)arg.count)) return false;
            for (uint32_t i = 0; i < arg.count; i++) {
                uint32_t handle = reader.ReadUInt32();
                void* object = NULL;
                if (handle != 0 && ResolveHandle(handle, &iid, &object) != S_OK)
                    failure = E_HANDLE;
                arg.objects.push_back(object);
            }
            return true;
        }
        case Arg_OutObjects:
            if (!reader.Has(4)) return false;
            arg.count = reader.ReadUInt32();
            arg.objects.assign(arg.count, NULL);
            return ReadProbes(reader, arg.probes);
        case Arg_Guid:
            if (!reader.Has(sizeof(GUID))) return false;
            arg.guid = reader.ReadGuid();
            return true;
        case Arg_String:
            if (!reader.Has(4)) return false;
            arg.count = reader.ReadUInt32();
            if (!reader.Has(2 * (size_t)arg.count)) return false;
            arg.text.assign(arg.count + 1, 0);
            reader.ReadBytes(arg.text.data(), 2 * (size_t)arg.count);
            return true;
        case Arg_OutBlob:
            if (!reader.Has(8)) return false;
            arg.unit = reader.ReadUInt32();
            arg.minimum = reader.ReadUInt32();
            return true;
        case Arg_OutRecords: {
            if (!reader.Has(9)) return false;
            arg.count = reader.ReadUInt32();
            arg.recordSize = reader.ReadUInt32();
            uint8_t pointers = reader.ReadByte();
            if (!reader.Has(4 * (size_t)pointers)) return false;
            for (uint8_t i = 0; i < pointers; i++)
                arg.offsets.push_back(reader.ReadUInt32());
            arg.bytes.assign((size_t)arg.count * arg.recordSize, 0);
            return true;
        }
        default:
            return false;
    }
}
// An empty array of objects still travels as a pointer to something: the runtime's debugging library refuses a NULL
// array even with a count of zero (a constructor call without arguments), as an in-process caller never passes one.
// Empty bytes stay NULL: a NULL signature tells the metadata importer to match by name alone (FindMethod, FindField)
static void* emptyArray[2];

// The words of the native call an argument stands for
static void PushWords(std::vector<Word>& words, CallArgument& arg) {
    switch (arg.kind) {
        case Arg_UInt32:
        case Arg_UInt64:
            words.push_back((Word)arg.value);
            break;
        case Arg_Object:
            words.push_back((Word)arg.object);
            break;
        // RefUInt64 carries an in/out value such as the HCORENUM of the metadata enumerations
        // https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadataimport-enumtypedefs-method
        case Arg_OutUInt32:
        case Arg_OutUInt64:
        case Arg_RefUInt64:
            words.push_back((Word)&arg.value);
            break;
        case Arg_OutObject:
            words.push_back((Word)&arg.object);
            break;
        // The debugging API's string pattern: ULONG32 cch, ULONG32* pcch, WCHAR* buffer, as in ICorDebugModule::GetName
        // https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmodule-getname-method
        case Arg_OutString:
            words.push_back((Word)arg.count);
            words.push_back((Word)&arg.length);
            words.push_back((Word)(arg.count > 0 ? arg.text.data() : NULL));
            break;
        // The metadata API's order of the same pattern: buffer, capacity, length, as in IMetaDataImport::GetTypeDefProps
        // https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadataimport-gettypedefprops-method
        case Arg_OutStringBuffer:
            words.push_back((Word)(arg.count > 0 ? arg.text.data() : NULL));
            words.push_back((Word)arg.count);
            words.push_back((Word)&arg.length);
            break;
        case Arg_Bytes:
        case Arg_OutBytes:
        case Arg_OutRecords:
            words.push_back((Word)(arg.bytes.empty() ? NULL : arg.bytes.data()));
            break;
        case Arg_Objects:
        case Arg_OutObjects:
            words.push_back((Word)(arg.objects.empty() ? (void*)emptyArray : arg.objects.data()));
            break;
        case Arg_Guid:
        case Arg_OutGuid:
            words.push_back((Word)&arg.guid);
            break;
        case Arg_String:
            words.push_back((Word)arg.text.data());
            break;
        // A pointer into the callee's memory plus a count, copied into the response: a signature
        // (IMetaDataImport::GetSigFromToken), a custom attribute blob, or a constant whose count is in characters
        // for a string and zero otherwise (IMetaDataImport::GetFieldProps), hence the unit and the minimum
        // https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadataimport-getsigfromtoken-method
        // https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadataimport-getfieldprops-method
        case Arg_OutBlob:
            words.push_back((Word)&arg.object);
            words.push_back((Word)&arg.value);
            break;
    }
}
// Whether the object has the interface: what the host's QueryInterface would find out
static bool HasInterface(void* object, const GUID* iid) {
    void* found = QueryInterface(object, iid);
    if (found == NULL)
        return false;
    Release(found);
    return true;
}
// An object the callee returned: its handle (0 for NULL), then one byte per probe saying whether it has the
// interface, so the host makes the right proxy without asking again. The reference the callee gave is the table's now
static void WriteObject(ByteWriter& result, void* object, const std::vector<GUID>& probes, bool succeeded) {
    uint32_t handle = succeeded ? RegisterObject(object, true) : 0;
    result.WriteUInt32(handle);
    for (size_t i = 0; i < probes.size(); i++)
        result.WriteByte(handle != 0 && HasInterface(object, &probes[i]) ? 1 : 0);
}
// What the callee wrote, in the response; zeroes when the call failed
static void WriteResult(ByteWriter& result, CallArgument& arg, bool succeeded) {
    switch (arg.kind) {
        case Arg_OutUInt32:
            result.WriteUInt32(succeeded ? (uint32_t)arg.value : 0);
            break;
        case Arg_OutUInt64:
            result.WriteUInt64(succeeded ? arg.value : 0);
            break;
        case Arg_RefUInt64:
            result.WriteUInt64(arg.value);
            break;
        case Arg_OutObject:
            WriteObject(result, arg.object, arg.probes, succeeded);
            break;
        case Arg_OutString:
        case Arg_OutStringBuffer: {
            uint32_t copied = 0;
            if (succeeded)
                copied = arg.length < arg.count ? arg.length : arg.count;
            result.WriteUInt32(succeeded ? arg.length : 0);
            result.WriteWide(arg.text.data(), copied);
            break;
        }
        case Arg_OutBytes:
            result.WriteUInt32(arg.count);
            result.WriteBytes(arg.bytes.data(), arg.count);
            break;
        case Arg_OutObjects:
            result.WriteUInt32(arg.count);
            for (size_t i = 0; i < arg.objects.size(); i++)
                WriteObject(result, arg.objects[i], arg.probes, succeeded);
            break;
        case Arg_OutGuid:
            result.WriteBytes(&arg.guid, sizeof(GUID));
            break;
        case Arg_OutBlob: {
            uint32_t count = succeeded ? (uint32_t)arg.value : 0;
            uint32_t size = 0;
            if (succeeded && arg.object != NULL) {
                size = count * arg.unit;
                if (size < arg.minimum)
                    size = arg.minimum;
            }
            result.WriteUInt32(count);
            result.WriteUInt32(size);
            if (size > 0)
                result.WriteBytes(arg.object, size);
            break;
        }
        // Records the callee filled in, with each interface pointer in them replaced by its handle (as a 64-bit
        // value in the pointer's place; the platforms the agent runs on have 64-bit pointers). The buffer started
        // zeroed, so a record the callee did not reach holds a NULL pointer and gets handle 0
        case Arg_OutRecords: {
            if (!succeeded)
                memset(arg.bytes.data(), 0, arg.bytes.size());
            for (uint32_t record = 0; record < arg.count && succeeded; record++) {
                for (size_t k = 0; k < arg.offsets.size(); k++) {
                    size_t at = (size_t)record * arg.recordSize + arg.offsets[k];
                    if (at + sizeof(void*) > arg.bytes.size())
                        continue;
                    void* pointer = NULL;
                    memcpy(&pointer, &arg.bytes[at], sizeof(void*));
                    uint64_t handle = RegisterObject(pointer, true);
                    memcpy(&arg.bytes[at], &handle, sizeof(handle));
                }
            }
            result.WriteUInt32((uint32_t)arg.bytes.size());
            result.WriteBytes(arg.bytes.data(), arg.bytes.size());
            break;
        }
        default:
            break;
    }
}
// The interfaces resolved for the call are references of this request only
static void ReleaseTemporaries(CallArgument& arg) {
    if (arg.kind == Arg_Object && arg.object != NULL)
        Release(arg.object);
    if (arg.kind == Arg_Objects) {
        for (size_t i = 0; i < arg.objects.size(); i++) {
            if (arg.objects[i] != NULL)
                Release(arg.objects[i]);
        }
    }
}

HRESULT HandleInvoke(ByteReader& reader, ByteWriter& result) {
    if (!reader.Has(4 + sizeof(GUID) + 2 + 1))
        return E_INVALIDARG;
    uint32_t handle = reader.ReadUInt32();
    GUID iid = reader.ReadGuid();
    uint16_t slot = reader.ReadUInt16();
    uint8_t count = reader.ReadByte();
    std::vector<CallArgument> args(count);
    HRESULT hr = S_OK;
    for (size_t i = 0; i < args.size(); i++) {
        if (!ReadArgument(reader, args[i], hr)) {
            hr = E_INVALIDARG;
            break;
        }
    }
    void* target = NULL;
    if (hr == S_OK)
        hr = ResolveHandle(handle, &iid, &target);
    if (hr == S_OK) {
        std::vector<Word> words;
        for (size_t i = 0; i < args.size(); i++)
            PushWords(words, args[i]);
        hr = CallMethod(target, slot, words.data(), (int)words.size());
        Release(target);
    }
    // A callee's failure is the host's business (a metadata lookup fails as a matter of course); a call that could not
    // be made at all is logged
    if (hr == E_HANDLE || hr == E_NOINTERFACE || hr == E_INVALIDARG)
        Log("invoke handle %u slot %u of %08x could not be made: 0x%08x", (unsigned)handle, (unsigned)slot, (unsigned)iid.Data1, (unsigned)hr);
    for (size_t i = 0; i < args.size(); i++) {
        WriteResult(result, args[i], hr >= 0);
        ReleaseTemporaries(args[i]);
    }
    return hr;
}
HRESULT HandleQuery(ByteReader& reader, ByteWriter& result) {
    if (!reader.Has(5))
        return E_INVALIDARG;
    uint32_t handleCount = reader.ReadUInt32();
    uint8_t iidCount = reader.ReadByte();
    if (!reader.Has(4 * (size_t)handleCount + sizeof(GUID) * (size_t)iidCount))
        return E_INVALIDARG;
    std::vector<uint32_t> handles;
    for (uint32_t i = 0; i < handleCount; i++)
        handles.push_back(reader.ReadUInt32());
    std::vector<GUID> iids;
    for (uint8_t i = 0; i < iidCount; i++)
        iids.push_back(reader.ReadGuid());
    for (size_t h = 0; h < handles.size(); h++) {
        for (size_t i = 0; i < iids.size(); i++) {
            void* object = NULL;
            bool supported = ResolveHandle(handles[h], &iids[i], &object) == S_OK;
            if (supported)
                Release(object);
            result.WriteByte(supported ? 1 : 0);
        }
    }
    return S_OK;
}
