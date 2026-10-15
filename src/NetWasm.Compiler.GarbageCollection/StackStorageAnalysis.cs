using System.Collections.Immutable;

namespace NetWasm.Compiler.GarbageCollection;

public sealed record StackStorageAnalysis(
    ImmutableDictionary<int, ImmutableArray<StackStorageType>> Entries,
    ImmutableDictionary<int, StackStorageTransfer> Transfers)
{
    public ImmutableDictionary<int, ImmutableArray<StackStorageType>> Locals { get; init; } =
        ImmutableDictionary<int, ImmutableArray<StackStorageType>>.Empty;

    public static StackStorageAnalysis Empty { get; } = new(
        ImmutableDictionary<int, ImmutableArray<StackStorageType>>.Empty,
        ImmutableDictionary<int, StackStorageTransfer>.Empty);
}
