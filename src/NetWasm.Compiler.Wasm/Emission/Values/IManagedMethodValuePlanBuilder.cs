namespace NetWasm.Compiler.Wasm.Emission.Values;

internal interface IManagedMethodValuePlanBuilder
{
    ManagedMethodValuePlan Build(ManagedMethodValuePlanRequest request);
}
