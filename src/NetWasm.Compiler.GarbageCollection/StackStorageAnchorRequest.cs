using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed record StackStorageAnchorRequest(
    ManagedMethodBody Method,
    StackStorageAnalysis Storage,
    ImmutableDictionary<int, ImmutableHashSet<StackStorageAnchor>> Demands);
