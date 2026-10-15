using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

// Offsets are meaningful only for these exact immutable method snapshots.
public sealed record ArrayBoundsProof(
    ImmutableArray<CilInstruction> Instructions,
    ImmutableArray<CliValueKind> Locals,
    ImmutableArray<CliTypeIdentity> LocalSignatureTypes,
    ImmutableHashSet<int> AccessOffsets)
{
    public ImmutableDictionary<int, ArrayIndexOffset> IndexOffsets { get; init; } = [];
}
