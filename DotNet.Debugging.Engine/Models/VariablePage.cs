namespace DotNet.Debugging.Engine.Models;

public class VariablePage {
    public List<VariableInfo> Variables { get; }
    public int TotalCount { get; }

    public VariablePage(List<VariableInfo> variables, int totalCount) {
        Variables = variables;
        TotalCount = totalCount;
    }
}
