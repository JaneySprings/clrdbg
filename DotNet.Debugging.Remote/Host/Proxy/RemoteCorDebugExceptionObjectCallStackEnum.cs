using System.Buffers.Binary;
using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Remote.Protocol;

namespace DotNet.Debugging.Remote.Proxy;

// The frames an exception object recorded as it was thrown and rethrown (ICorDebugExceptionObjectValue). Each record
// the enumerator fills holds a module pointer, the native instruction pointer, the method token and a flag; the agent
// returns the records with the module pointer replaced by its handle
[GeneratedComClass]
internal partial class RemoteCorDebugExceptionObjectCallStackEnum : RemoteObject, ICorDebugExceptionObjectCallStackEnum {
    // CorDebugExceptionObjectStackFrame as the app lays it out (64-bit): ICorDebugModule* at 0, CORDB_ADDRESS at 8,
    // mdMethodDef at 16, BOOL at 20
    private const uint RecordSize = 24;

    public RemoteCorDebugExceptionObjectCallStackEnum(RemoteSession session, uint handle) : base(session, handle) { }

    public int TryNext(uint celt, CorDebugExceptionObjectStackFrame[] values, out uint pceltFetched) {
        var records = RemoteArgument.OutRecords(celt, RecordSize, [0]);
        var fetched = RemoteArgument.OutUInt32();
        // Slot 7: after IUnknown's three and ICorDebugEnum's Skip, Reset, Clone and GetCount
        var hr = Invoke(Iids.ExceptionObjectCallStackEnum, 7, RemoteArgument.UInt32(celt), records, fetched);
        pceltFetched = fetched.UInt32Result;
        // S_FALSE when fewer frames than asked were left
        if (hr < 0 || values == null)
            return hr;

        var count = (int)Math.Min(pceltFetched, (uint)values.Length);
        var handles = new uint[count];
        for (var i = 0; i < count; i++)
            handles[i] = (uint)BinaryPrimitives.ReadUInt64LittleEndian(records.BytesResult.AsSpan(i * (int)RecordSize));
        var modules = Session.GetProxies<ICorDebugModule>(handles);
        for (var i = 0; i < count; i++) {
            var record = records.BytesResult.AsSpan(i * (int)RecordSize);
            values[i] = new CorDebugExceptionObjectStackFrame {
                pModule = modules[i],
                ip = new CordbAddress(BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(8))),
                methodDef = new MethodDefToken(BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(16))),
                isLastForeignExceptionFrame = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(20)),
            };
        }
        return hr;
    }
}
