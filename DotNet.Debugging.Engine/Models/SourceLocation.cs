namespace DotNet.Debugging.Engine.Models;

// Lines and columns are 1-based
public class SourceLocation {
    // The document path as recorded in the PDB
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public int EndLine { get; }
    public int EndColumn { get; }
    public SourceChecksum? Checksum { get; set; }
    // The URL the document can be downloaded from, when the PDB carries a SourceLink map for it
    public string? SourceLink { get; set; }

    public SourceLocation(string filePath, int line, int column, int endLine, int endColumn) {
        FilePath = filePath;
        Line = line;
        Column = column;
        EndLine = endLine;
        EndColumn = endColumn;
    }
}
