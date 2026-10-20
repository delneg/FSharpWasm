using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal static class ArrayMetadataServiceCollectionExtensions
{
    public static IServiceCollection AddArrayMetadataCaching(this IServiceCollection services)
    {
        services.AddSingleton<IArrayMetadataCachingPlanner, ArrayMetadataCachingPlanner>();
        services.AddSingleton<IArrayStoreCheckPlanner, ArrayStoreCheckPlanner>();
        services.AddSingleton<IArrayMetadataSnapshotEmitter, ArrayMetadataSnapshotEmitter>();
        services.AddSingleton<IArrayElementAddressEmitter, ArrayElementAddressEmitter>();
        return services;
    }
}
