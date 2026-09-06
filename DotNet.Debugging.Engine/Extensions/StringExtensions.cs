namespace DotNet.Debugging.Engine.Extensions;

internal static class StringExtensions {
    public static string? NullIfWhiteSpace(this string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
    public static string NormalizePathSeparators(this string path) {
        return path.Replace('\\', '/');
    }
}
