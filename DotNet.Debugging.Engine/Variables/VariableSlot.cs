using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Variables;

// A variable the listing holds but has not read yet: its name comes from metadata alone, its value is only
// read from the debuggee once the page it falls into is requested
internal class VariableSlot {
    private readonly Func<Task<VariableInfo?>>? create;
    private readonly VariableInfo? variable;

    // The name the listing is sorted and paged by, known without touching the debuggee
    public string Name { get; }

    public VariableSlot(string name, Func<Task<VariableInfo?>> create) {
        Name = name;
        this.create = create;
    }
    // A variable that costs nothing to produce (a literal, a group node, a member that failed while the listing was built)
    public VariableSlot(VariableInfo variable) {
        Name = variable.Name;
        this.variable = variable;
    }

    // A member that cannot be read is shown with the error as its value
    public async Task<VariableInfo?> MaterializeAsync() {
        if (variable != null)
            return variable;
        try {
            return await create!();
        }
        catch (Exception ex) {
            return VariableInfo.CreateError(Name, ex.Message);
        }
    }
}
