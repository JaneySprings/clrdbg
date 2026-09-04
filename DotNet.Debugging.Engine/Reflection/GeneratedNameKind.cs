namespace DotNet.Debugging.Engine.Reflection;

// The kinds of compiler generated names the engine tells apart, named as Roslyn names them
// (Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameKind) and matched by name, so their values need not agree
internal enum GeneratedNameKind {
    None,
    // A kind the engine has no use for
    Other,
    ThisProxyField,
    HoistedLocalField,
    DisplayClassLocalOrField,
    LambdaDisplayClass,
    StateMachineType,
}
