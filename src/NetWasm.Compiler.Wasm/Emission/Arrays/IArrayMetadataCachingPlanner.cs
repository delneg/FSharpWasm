using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal interface IArrayMetadataCachingPlanner
{
    ArrayMetadataCachingPlan Plan(StructuredMethod method, bool enabled, bool hoistNullChecks = false);
}
