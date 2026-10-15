using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.CallLifetimes;

internal sealed record ScalarCallLifetimePlan(
    ImmutableDictionary<int, ImmutableArray<int>> CallLocals)
{
    public static ScalarCallLifetimePlan Empty { get; } = new(
        ImmutableDictionary<int, ImmutableArray<int>>.Empty);
}
