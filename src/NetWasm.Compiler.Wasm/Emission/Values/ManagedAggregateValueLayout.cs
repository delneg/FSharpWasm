using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed record ManagedAggregateValueLayout(
    ManagedAggregateSsaPlan Plan,
    ImmutableDictionary<ManagedAggregateSsaLane, int> LaneLocals)
{
    public static ManagedAggregateValueLayout Empty { get; } =
        new(ManagedAggregateSsaPlan.Empty, []);
}
