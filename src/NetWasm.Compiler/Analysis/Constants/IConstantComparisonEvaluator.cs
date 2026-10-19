using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal interface IConstantComparisonEvaluator
{
    // Arguments include the receiver of an instance method.
    ScalarConstant? Evaluate(MethodInstanceModel method, IReadOnlyList<ScalarConstant> arguments);
}
