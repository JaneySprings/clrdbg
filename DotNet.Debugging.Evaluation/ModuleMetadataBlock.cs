using System;

namespace DotNet.Debugging.Evaluation;

// A loaded module the way the expression compiler binds against it: the identity it knows the module by and where
// the module's raw metadata lives in the debugger's memory
public class ModuleMetadataBlock {
    public Guid Mvid { get; }
    public string Name { get; }
    public Guid GenerationId { get; }
    public nint Pointer { get; }
    public int Size { get; }

    public ModuleMetadataBlock(Guid mvid, string name, Guid generationId, nint pointer, int size) {
        Mvid = mvid;
        Name = name;
        GenerationId = generationId;
        Pointer = pointer;
        Size = size;
    }
}
