using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.LoopInvariantFields;

internal sealed record AggregateFieldHoist(
    int PreheaderTerminatorOffset,
    int SourceLocalIndex,
    int CachedLocalIndex,
    CliTypeIdentity FieldType,
    CilInstruction AddressLoad,
    CilInstruction Projection,
    ImmutableArray<int> AddressOffsets,
    ImmutableArray<int> ProjectionOffsets);

internal sealed record AggregateFieldHoistingPlan(
    ImmutableArray<AggregateFieldHoist> Hoists)
{
    public static AggregateFieldHoistingPlan Empty { get; } = new([]);
}
