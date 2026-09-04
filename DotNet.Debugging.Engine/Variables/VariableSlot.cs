using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Variables;

// A variable the listing holds but has not read yet: its name comes from metadata alone, its value is only
// read from the debuggee once the page it falls into is requested. A slot stands for one entry, or for a
// block of entries named and read by their offset (the elements of an array), so a listing holds one slot
// per array rather than one per element
internal class VariableSlot {
    private readonly Func<int, Task<VariableInfo?>>? create;
    private readonly Func<int, string>? getEntryName;
    private readonly VariableInfo? variable;

    // The name the listing is sorted and paged by, known without touching the debuggee; a block goes by its first entry
    public string Name { get; }
    // The entries the slot stands for
    public int Count { get; }

    public VariableSlot(string name, Func<Task<VariableInfo?>> create) {
        Name = name;
        Count = 1;
        this.create = _ => create();
    }
    public VariableSlot(int count, Func<int, string> getEntryName, Func<int, Task<VariableInfo?>> create) {
        Name = getEntryName(0);
        Count = count;
        this.getEntryName = getEntryName;
        this.create = create;
    }
    // A variable that costs nothing to produce (a literal, a group node, a member that failed while the listing was built)
    public VariableSlot(VariableInfo variable) {
        Name = variable.Name;
        Count = 1;
        this.variable = variable;
    }

    // A member that cannot be read is shown with the error as its value
    public async Task<VariableInfo?> MaterializeAsync(int offset = 0) {
        if (variable != null)
            return variable;
        try {
            return await create!(offset);
        }
        catch (Exception ex) {
            return VariableInfo.CreateError(getEntryName == null ? Name : getEntryName(offset), ex.Message);
        }
    }
}
