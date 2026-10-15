using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

public sealed record StackStorageAnchorPlan(
    ImmutableArray<StackStorageAnchor> Anchors,
    ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>> Assignments)
{
    public static StackStorageAnchorPlan Empty { get; } = new([], ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty);
}
