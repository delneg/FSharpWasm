using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed record AddressTakenScalarPromotionLayout(
    AddressTakenScalarPromotionPlan Plan,
    ImmutableDictionary<int, int> ShadowLocals)
{
    public static AddressTakenScalarPromotionLayout Empty { get; } = new(
        AddressTakenScalarPromotionPlan.Empty,
        []);
}
