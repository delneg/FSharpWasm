using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimePackTargetSelector
{
    RuntimePackTarget Select(RuntimePackManifest manifest, string target, string? garbageCollector,
        RuntimeWasmOptimization optimization = RuntimeWasmOptimization.Oz);
}
