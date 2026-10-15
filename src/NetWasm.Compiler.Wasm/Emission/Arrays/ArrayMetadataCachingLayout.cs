using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal sealed record ArrayMetadataSnapshotLocals(int Length, int Data);

internal sealed record ArrayMetadataCachingLayout(
    ArrayMetadataCachingPlan Plan,
    ImmutableDictionary<ArrayReferenceSlot, ArrayMetadataSnapshotLocals> Slots)
{
    public static ArrayMetadataCachingLayout Empty { get; } = new(ArrayMetadataCachingPlan.Empty, []);
}
