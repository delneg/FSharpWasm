namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal interface IAddressTakenAggregatePromotionPlanner
{
    AddressTakenAggregatePromotionPlan Plan(
        AddressTakenAggregatePromotionRequest request);
}
