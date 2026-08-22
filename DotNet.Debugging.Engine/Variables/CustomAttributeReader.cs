using System.Reflection.Metadata;

namespace DotNet.Debugging.Engine.Variables;

// Decodes custom attribute blobs (ECMA-335 II.23.3)
internal static class CustomAttributeReader {
    private const ushort Prolog = 0x0001;
    // The 'Type' argument kind of a named argument (serialized as a string)
    private const byte TypeSignatureTypeCode = 0x50;

    public static unsafe int ReadInt32Argument(nint data, uint size) {
        var reader = new BlobReader((byte*)data, checked((int)size));
        ReadProlog(ref reader);
        return reader.ReadInt32();
    }
    public static unsafe string? ReadStringArgument(nint data, uint size) {
        var reader = new BlobReader((byte*)data, checked((int)size));
        ReadProlog(ref reader);
        return reader.ReadSerializedString();
    }
    // The value of a string (or Type) named argument following a single string constructor argument
    public static unsafe string? ReadNamedStringArgument(nint data, uint size, string argumentName) {
        var reader = new BlobReader((byte*)data, checked((int)size));
        ReadProlog(ref reader);
        reader.ReadSerializedString();

        var namedArgumentCount = reader.ReadUInt16();
        for (var i = 0; i < namedArgumentCount; i++) {
            reader.ReadByte(); // field or property
            var typeCode = reader.ReadByte();
            if (typeCode != (byte)SignatureTypeCode.String && typeCode != TypeSignatureTypeCode)
                return null;

            var name = reader.ReadSerializedString();
            var value = reader.ReadSerializedString();
            if (name == argumentName)
                return value;
        }
        return null;
    }

    private static void ReadProlog(ref BlobReader reader) {
        if (reader.ReadUInt16() != Prolog)
            throw new InvalidOperationException("Invalid custom attribute prolog");
    }
}
