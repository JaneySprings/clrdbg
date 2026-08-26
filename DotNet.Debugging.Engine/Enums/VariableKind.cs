namespace DotNet.Debugging.Engine.Enums;

public enum VariableKind {
    Data,
    Property,
    // A pseudo node grouping other variables ('Static members', 'Non-Public members', 'Raw View')
    Group,
    // The 'Results View' node of a value implementing IEnumerable
    ResultsView,
}
