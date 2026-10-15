using System.Collections.Immutable;

namespace NetWasm.Compiler.GarbageCollection;

public readonly record struct StackStorageFlowEdge(int Block, bool ResetStack);

public sealed record StackStorageContinuations(
    ImmutableDictionary<int, ImmutableArray<StackStorageFlowEdge>> Normal,
    ImmutableDictionary<int, ImmutableArray<StackStorageFlowEdge>> Exceptional);
