using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed record ManagedMethodValuePlan(
    MethodRootMap EmissionRootMap,
    ManagedAggregateSsaPlan Aggregates)
{
    public static ManagedMethodValuePlan Unchanged(MethodRootMap rootMap) =>
        new(rootMap, ManagedAggregateSsaPlan.Empty);
}

internal sealed record ManagedMethodValuePlanRequest(
    StructuredMethod Method,
    MethodRootMap RootMap,
    ManagedByRefEffects ByRefEffects,
    ImmutableHashSet<ManagedAggregateStorageId> ReservedStorages,
    bool Enabled);
