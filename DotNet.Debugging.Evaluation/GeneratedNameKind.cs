namespace DotNet.Debugging.Evaluation;

// The kinds of compiler generated names the engine tells apart, named as Roslyn names them
public enum GeneratedNameKind {
    None,
    // A kind the engine has no use for
    Other,
    ThisProxyField,
    HoistedLocalField,
    DisplayClassLocalOrField,
    LambdaDisplayClass,
    StateMachineType,
}
