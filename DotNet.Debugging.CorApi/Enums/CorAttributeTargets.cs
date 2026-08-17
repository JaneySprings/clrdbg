namespace DotNet.Debugging.CorApi;

public enum CorAttributeTargets {
    catAssembly = 1,
    catModule = 2,
    catClass = 4,
    catStruct = 8,
    catEnum = 16,
    catConstructor = 32,
    catMethod = 64,
    catProperty = 128,
    catField = 256,
    catEvent = 512,
    catInterface = 1024,
    catParameter = 2048,
    catDelegate = 4096,
    catGenericParameter = 16384,
    catAll = 24575,
    catClassMembers = 6140
}