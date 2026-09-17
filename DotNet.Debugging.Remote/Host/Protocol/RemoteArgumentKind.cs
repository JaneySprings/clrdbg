namespace DotNet.Debugging.Remote.Protocol;

// How an argument of an Invoke request travels; the agent expands each kind into the words of the native call
public enum RemoteArgumentKind : byte {
    UInt32 = 1,
    UInt64 = 2,
    Object = 3,
    OutUInt32 = 4,
    OutUInt64 = 5,
    OutObject = 6,
    OutText = 7,
    Bytes = 8,
    OutBytes = 9,
    Objects = 10,
    OutObjects = 11,
    Guid = 12,
    Text = 13,
    OutTextBuffer = 14,
    RefUInt64 = 15,
    OutBlob = 16,
    OutGuid = 17,
    OutRecords = 18,
}
