using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal interface IManagedInliningResolver
{
    ManagedInliningPlan? Resolve(ManagedInliningRequest request);
}

internal sealed record ManagedInliningRequest(
    MethodInstanceModel Caller,
    CilInstruction Call);

internal sealed record ManagedInliningPlan(
    MethodInstanceModel Method,
    CilMethodBody Body)
{
    public ImmutableDictionary<int, ImmutableArray<CliValueKind>>
        InstructionEntryStacks
    { get; init; } =
        ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;
}
