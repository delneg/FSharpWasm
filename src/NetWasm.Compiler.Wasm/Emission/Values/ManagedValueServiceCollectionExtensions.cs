using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal static class ManagedValueServiceCollectionExtensions
{
    public static IServiceCollection AddManagedValuePlanning(
        this IServiceCollection services)
    {
        services.AddSingleton<IRootSlotAllocator, RootSlotAllocator>();
        services.AddSingleton<IManagedAggregateValueAnalyzer,
            ManagedAggregateValueAnalyzer>();
        services.AddSingleton<IManagedAggregateSsaPlanner,
            ManagedAggregateSsaPlanner>();
        services.AddSingleton<IManagedAggregateRootMapProjector,
            ManagedAggregateRootMapProjector>();
        services.AddSingleton<IManagedMethodValuePlanBuilder,
            ManagedMethodValuePlanBuilder>();
        services.AddSingleton<IManagedAggregateValueInitializer,
            ManagedAggregateValueInitializer>();
        services.AddSingleton<IManagedAggregateValueInstructionEmitter,
            ManagedAggregateValueInstructionEmitter>();
        services.AddSingleton<IManagedAggregateValueReloadEmitter,
            ManagedAggregateValueReloadEmitter>();
        return services;
    }
}
