namespace NetWasm.Compiler.Wasm.Emission.Values;

internal interface IManagedAggregateSsaPlanner
{
    ManagedAggregateSsaPlan Plan(ManagedAggregateSsaPlanningRequest request);
}
