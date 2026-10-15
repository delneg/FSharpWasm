using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeOptimizationPolicyResolver
{
    RuntimeOptimizationPolicy Resolve(RuntimeWasmOptimization optimization);
}
