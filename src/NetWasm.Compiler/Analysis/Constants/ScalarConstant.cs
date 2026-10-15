using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal abstract record ScalarConstant
{
    internal sealed record Int32(int Value) : ScalarConstant;
    internal sealed record Null : ScalarConstant;
}

internal sealed record ConstantCallEvaluation(
    MethodInstanceModel Method,
    ImmutableArray<ScalarConstant?> Arguments);
