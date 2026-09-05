using System;
using Microsoft.CodeAnalysis.ExpressionEvaluator;

namespace DotNet.Debugging.Evaluation;

// The module, method and IL span a method context stays valid for: the same locals are in scope at every offset of
// the span, so an expression compiled there need not be recompiled while stepping through it
public class ReuseConstraints {
    private readonly MethodContextReuseConstraints constraints;

    internal ReuseConstraints(MethodContextReuseConstraints constraints) {
        this.constraints = constraints;
    }

    public bool AreSatisfied(Guid mvid, string moduleName, int methodToken, int ilOffset) {
        return constraints.AreSatisfied(new ModuleId(mvid, moduleName), methodToken, ExpressionContext.MethodVersion, ilOffset);
    }
}
