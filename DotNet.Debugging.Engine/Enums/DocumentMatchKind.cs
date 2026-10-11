namespace DotNet.Debugging.Engine.Enums;

internal enum DocumentMatchKind {
    // The document's path, through the source file map, is the file's path
    Path,
    // An equally named document elsewhere: a PDB built from a location the map does not cover
    Name,
}
