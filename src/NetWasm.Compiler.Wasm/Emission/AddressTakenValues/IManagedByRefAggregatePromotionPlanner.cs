namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal interface IManagedByRefAggregatePromotionPlanner
{
    ManagedByRefAggregatePromotionPlan Plan(
        ManagedByRefAggregatePromotionRequest request);
}
