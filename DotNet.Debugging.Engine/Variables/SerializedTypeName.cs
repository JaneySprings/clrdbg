namespace DotNet.Debugging.Engine.Variables;

// A type name as custom attributes store it (ECMA-335 II.23.3), the format DebuggerTypeProxyAttribute uses:
// 'Ns.Open`1' for an open generic proxy and 'Ns.Closed`1[Ns.Arg]' for a closed one, where an argument living in
// another assembly is bracketed with its qualifier ('[Ns.Arg, Assembly, Version=...]') and the whole name may
// end with a ', Assembly' qualifier of its own - the qualifiers are dropped, the loaded modules are searched instead
internal class SerializedTypeName {
    public string FullName { get; }
    public IReadOnlyList<SerializedTypeName> TypeArguments { get; }

    private SerializedTypeName(string fullName, IReadOnlyList<SerializedTypeName> typeArguments) {
        FullName = fullName;
        TypeArguments = typeArguments;
    }

    public static SerializedTypeName Parse(string text) {
        var position = 0;
        return ParseName(text, ref position);
    }

    private static SerializedTypeName ParseName(string text, ref int position) {
        var fullName = ParseFullName(text, ref position);
        var typeArguments = new List<SerializedTypeName>();
        if (position < text.Length && text[position] == '[') {
            position++;
            while (position < text.Length && text[position] != ']') {
                if (text[position] == '[') {
                    // A bracketed argument, the brackets enclose the argument and its assembly qualifier
                    position++;
                    typeArguments.Add(ParseName(text, ref position));
                    while (position < text.Length && text[position] != ']')
                        position++;
                    position++;
                }
                else {
                    typeArguments.Add(ParseName(text, ref position));
                }
                if (position < text.Length && text[position] == ',')
                    position++;
            }
            position++; // The ']' closing the argument list
        }
        // A ',' left at the position starts the assembly qualifier of this name, the caller skips it
        return new SerializedTypeName(fullName, typeArguments);
    }
    private static string ParseFullName(string text, ref int position) {
        var start = position;
        while (position < text.Length && text[position] != '[' && text[position] != ']' && text[position] != ',')
            position++;
        // An immediately closed bracket is an array suffix ('Ns.Arg[]', 'Ns.Arg[,]') rather than an argument
        // list - it stays part of the name, which type resolution then reports as not found
        while (position + 1 < text.Length && text[position] == '[' && (text[position + 1] == ']' || text[position + 1] == ',')) {
            while (position < text.Length && text[position] != ']')
                position++;
            position++;
        }
        return text.Substring(start, position - start).Trim();
    }
}
