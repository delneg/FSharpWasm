using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed record ManagedByRefAggregateParameterPlan(
    CliTypeIdentity Type,
    int Size,
    ImmutableArray<AddressTakenAggregateStorageSlot> StorageSlots);

internal sealed record ManagedByRefAggregateProjection(
    int ParameterIndex,
    int ByteOffset,
    int Size,
    CliValueKind StackKind,
    bool AddressAlreadyProjected);

internal sealed record ManagedByRefAggregatePromotionPlan(
    ImmutableDictionary<int, ManagedByRefAggregateParameterPlan> PromotedParameters,
    ImmutableDictionary<int, ManagedByRefAggregateProjection> Projections)
{
    public static ManagedByRefAggregatePromotionPlan Empty { get; } = new([], []);
}

internal sealed record ManagedByRefAggregatePromotionLayout(
    ManagedByRefAggregatePromotionPlan Plan,
    ImmutableDictionary<int, ImmutableArray<int>> StorageLocals)
{
    public static ManagedByRefAggregatePromotionLayout Empty { get; } =
        new(ManagedByRefAggregatePromotionPlan.Empty, []);
}
