namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal interface IAddressTakenScalarPromotionPlanner
{
    AddressTakenScalarPromotionPlan Plan(AddressTakenScalarPromotionRequest request);
}
