using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

public class VariableInfo {
    public string Name { get; }
    public string Value { get; }
    public string? Type { get; }
    public VariableKind Kind { get; set; }
    public VariableVisibility? Visibility { get; set; }
    // The expression that yields this variable ('item.Field', 'numbers[0]'), null for pseudo nodes
    public string? EvaluateName { get; set; }
    // Non-zero when the variable has children
    public int VariablesReference { get; set; }
    // 'Value' holds the error text
    public bool IsError { get; set; }

    public VariableInfo(string name, string value, string? type) {
        Name = name;
        Value = value;
        Type = type;
    }

    public static VariableInfo CreateError(string name, string error) {
        var variable = new VariableInfo(name, error, null);
        variable.IsError = true;
        return variable;
    }
}
