using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface ITransientAggregatePromotionPlanner
{
    TransientAggregatePromotionPlan Create(StructuredMethod method);
}
