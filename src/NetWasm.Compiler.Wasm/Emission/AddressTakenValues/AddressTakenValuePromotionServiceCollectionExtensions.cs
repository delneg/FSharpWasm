using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal static class AddressTakenValuePromotionServiceCollectionExtensions
{
    public static IServiceCollection AddAddressTakenValuePromotion(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IAddressTakenScalarPromotionPlanner,
            AddressTakenScalarPromotionPlanner>();
        services.TryAddSingleton<IAddressTakenScalarSynchronizationEmitter,
            AddressTakenScalarSynchronizationEmitter>();
        services.TryAddSingleton<IAddressTakenAggregatePromotionPlanner,
            AddressTakenAggregatePromotionPlanner>();
        services.TryAddSingleton<IManagedByRefAggregatePromotionPlanner,
            ManagedByRefAggregatePromotionPlanner>();
        services.TryAddSingleton<IManagedByRefAggregateInitializationEmitter,
            ManagedByRefAggregateInitializationEmitter>();
        services.TryAddSingleton<IAddressTakenAggregateProjectionEmitter,
            AddressTakenAggregateProjectionEmitter>();
        services.TryAddSingleton<IAddressTakenAggregateSynchronizationEmitter,
            AddressTakenAggregateSynchronizationEmitter>();
        return services;
    }
}
