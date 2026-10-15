using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed record AddressTakenScalarPromotionPlan(
    ImmutableDictionary<int, CliTypeIdentity> PromotedLocals,
    ImmutableDictionary<int, AddressTakenScalarCallSynchronization> CallSynchronizations)
{
    public static AddressTakenScalarPromotionPlan Empty { get; } = new([], []);
}

internal sealed record AddressTakenScalarCallSynchronization(
    ImmutableArray<int> LocalIndices,
    ImmutableArray<int> ReloadLocalIndices);
