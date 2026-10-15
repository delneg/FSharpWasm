using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal interface IManagedInliningBodyValidator
{
    ImmutableDictionary<int, ImmutableArray<CliValueKind>> Validate(
        CilMethodBody body);
}
