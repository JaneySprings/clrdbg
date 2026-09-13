using System.Text;
using System.Text.RegularExpressions;

namespace DotNet.Debugging.Engine.Variables;

// One piece of a DebuggerDisplay string: literal text, or an expression in braces to evaluate against the value
internal class DebuggerDisplayPart {
    public string Text { get; }
    public bool IsExpression { get; }
    // The 'nq' format specifier: a string result is shown without its quotes
    public bool NoQuotes { get; }

    public DebuggerDisplayPart(string text, bool isExpression, bool noQuotes) {
        Text = text;
        IsExpression = isExpression;
        NoQuotes = noQuotes;
    }
}

// Splits a DebuggerDisplay string ('{Amount} {Currency,nq}', '\{ Id = {Id} }') into its literal text and its expressions
internal static class DebuggerDisplayTemplate {
    private static readonly Regex specifierListRegex = new Regex(@"\G\s*[A-Za-z]+(\s*,\s*[A-Za-z]+)*\s*$", RegexOptions.Compiled);

    public static List<DebuggerDisplayPart> Parse(string template) {
        var parts = new List<DebuggerDisplayPart>();
        var literal = new StringBuilder();
        var position = 0;
        while (position < template.Length) {
            var symbol = template[position];
            // A backslash escapes the next character, which is how a literal brace is written ('\{', '\}')
            if (symbol == '\\' && position + 1 < template.Length) {
                literal.Append(template[position + 1]);
                position += 2;
                continue;
            }
            if (symbol != '{') {
                literal.Append(symbol);
                position++;
                continue;
            }
            var end = FindExpressionEnd(template, position + 1);
            // An unterminated brace is text like any other
            if (end < 0) {
                literal.Append(template, position, template.Length - position);
                break;
            }
            if (literal.Length > 0) {
                parts.Add(new DebuggerDisplayPart(literal.ToString(), false, false));
                literal.Clear();
            }
            parts.Add(CreateExpressionPart(template.Substring(position + 1, end - position - 1)));
            position = end + 1;
        }
        if (literal.Length > 0)
            parts.Add(new DebuggerDisplayPart(literal.ToString(), false, false));
        return parts;
    }

    // The index of the brace closing the expression that starts at 'start', past nested braces and string literals; -1 when there is none
    private static int FindExpressionEnd(string template, int start) {
        var depth = 0;
        var quote = '\0';
        for (var i = start; i < template.Length; i++) {
            var symbol = template[i];
            if (quote != '\0') {
                if (symbol == '\\')
                    i++;
                else if (symbol == quote)
                    quote = '\0';
                continue;
            }
            if (symbol == '"' || symbol == '\'')
                quote = symbol;
            else if (symbol == '{')
                depth++;
            else if (symbol == '}' && depth-- == 0)
                return i;
        }
        return -1;
    }
    // 'Currency,nq': format specifiers follow a comma outside any brackets, 'nq' drops the quotes of a string, the others are ignored
    private static DebuggerDisplayPart CreateExpressionPart(string text) {
        var depth = 0;
        for (var i = 0; i < text.Length; i++) {
            if (text[i] is '(' or '[' or '<')
                depth++;
            else if (text[i] is ')' or ']' or '>')
                depth--;
            else if (text[i] == ',' && depth == 0 && specifierListRegex.IsMatch(text, i + 1)) {
                var noQuotes = text.Substring(i + 1).Split(',').Any(it => it.Trim().Equals("nq", StringComparison.OrdinalIgnoreCase));
                return new DebuggerDisplayPart(text.Substring(0, i).Trim(), true, noQuotes);
            }
        }
        return new DebuggerDisplayPart(text.Trim(), true, false);
    }
}
