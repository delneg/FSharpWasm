using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal enum ManagedAggregateStorageKind
{
    Argument,
    Local,
}

internal readonly record struct ManagedAggregateStorageId(
    ManagedAggregateStorageKind Kind,
    int Index);

internal sealed record ManagedAggregateStorage(
    ManagedAggregateStorageId Id,
    CliTypeIdentity Type,
    int Size,
    bool Owned);

internal readonly record struct ManagedAggregateAddressRecipe(
    ManagedAggregateStorageId Storage,
    int ByteOffset,
    CliTypeIdentity Type);

internal sealed record ManagedAggregateLaneAccess(
    int InstructionOffset,
    StructuredBlockId Block,
    ManagedAggregateAddressRecipe Address,
    int Size,
    CliValueKind StackKind,
    bool Writes);

internal enum ManagedAggregateRootSetKind
{
    Ordinary,
    Constructor,
}

internal sealed record ManagedAggregateRootDependency(
    int SafepointOffset,
    RootSource Source,
    ManagedAggregateAddressRecipe Address,
    ManagedAggregateRootSetKind Set);

internal sealed record ManagedAggregateMaterialization(
    int InstructionOffset,
    ManagedAggregateStorageId Storage,
    bool ReloadAfter = false);

internal sealed record ManagedAggregateValueAnalysis(
    ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage> Storages,
    ImmutableDictionary<int, ManagedAggregateLaneAccess> LaneAccesses,
    ImmutableDictionary<StructuredBlockId, ImmutableHashSet<ManagedAggregateStorageId>>
        LiveInAddresses,
    ImmutableArray<ManagedAggregateRootDependency> RootDependencies,
    ImmutableArray<ManagedAggregateMaterialization> Materializations)
{
    public static ManagedAggregateValueAnalysis Empty { get; } =
        new([], [], [], [], []);
}

internal sealed record ManagedAggregateValueAnalysisRequest(
    StructuredMethod Method,
    MethodRootMap RootMap,
    ManagedByRefEffects ByRefEffects,
    bool Enabled);
