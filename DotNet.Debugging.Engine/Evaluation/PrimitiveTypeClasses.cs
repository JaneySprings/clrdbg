using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;

namespace DotNet.Debugging.Engine.Evaluation;

// The System.Private.CoreLib classes of the primitive value types, needed to box primitives computed
// by the interpreter before they can be passed to the debuggee
internal class PrimitiveTypeClasses {
    private static readonly (CorElementType ElementType, string TypeName)[] primitiveTypeNames = [
        (CorElementType.BOOLEAN, "System.Boolean"),
        (CorElementType.CHAR, "System.Char"),
        (CorElementType.I1, "System.SByte"),
        (CorElementType.U1, "System.Byte"),
        (CorElementType.I2, "System.Int16"),
        (CorElementType.U2, "System.UInt16"),
        (CorElementType.I4, "System.Int32"),
        (CorElementType.U4, "System.UInt32"),
        (CorElementType.I8, "System.Int64"),
        (CorElementType.U8, "System.UInt64"),
        (CorElementType.R4, "System.Single"),
        (CorElementType.R8, "System.Double"),
    ];

    private readonly Dictionary<CorElementType, ICorDebugClass> classes;
    private readonly CordbAddress moduleBaseAddress;

    private PrimitiveTypeClasses(Dictionary<CorElementType, ICorDebugClass> classes, CordbAddress moduleBaseAddress) {
        this.classes = classes;
        this.moduleBaseAddress = moduleBaseAddress;
    }

    public static PrimitiveTypeClasses Load(ICorDebugModule coreLibModule) {
        var metadataImport = coreLibModule.GetMetaDataInterface<IMetaDataImport>();
        var classes = new Dictionary<CorElementType, ICorDebugClass>();
        foreach (var (elementType, typeName) in primitiveTypeNames) {
            var typeDef = metadataImport.FindTypeDef(typeName, MetadataToken.Nil);
            if (typeDef == null || typeDef.Value.IsNil)
                throw new InvalidOperationException($"Could not find the {typeName} type definition");
            classes[elementType] = coreLibModule.GetClassFromToken(typeDef.Value);
        }
        return new PrimitiveTypeClasses(classes, coreLibModule.GetBaseAddress());
    }

    public bool TryGetClass(CorElementType elementType, out ICorDebugClass corClass) {
        return classes.TryGetValue(elementType, out corClass!);
    }
    public bool IsPrimitiveClass(ICorDebugClass corClass) {
        if (corClass.GetModule().GetBaseAddress() != moduleBaseAddress)
            return false;
        var token = corClass.GetToken();
        return classes.Values.Any(it => it.GetToken() == token);
    }
}
