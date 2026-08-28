namespace DotNet.Debugging.Engine.Metadata;

internal static class AttributeNames {
    public const string DebuggerNonUserCode = "System.Diagnostics.DebuggerNonUserCodeAttribute";
    public const string DebuggerStepThrough = "System.Diagnostics.DebuggerStepThroughAttribute";
    public const string DebuggerHidden = "System.Diagnostics.DebuggerHiddenAttribute";
    public const string DebuggerBrowsable = "System.Diagnostics.DebuggerBrowsableAttribute";
    public const string DebuggerDisplay = "System.Diagnostics.DebuggerDisplayAttribute";
    public const string DebuggerTypeProxy = "System.Diagnostics.DebuggerTypeProxyAttribute";
    public const string StackTraceHidden = "System.Diagnostics.StackTraceHiddenAttribute";
    public const string Extension = "System.Runtime.CompilerServices.ExtensionAttribute";
    public const string Flags = "System.FlagsAttribute";

    // Methods the stepper must not stop in
    public static readonly string[] NonUserCodeMethodAttributes = [DebuggerNonUserCode, DebuggerStepThrough, DebuggerHidden];
}
