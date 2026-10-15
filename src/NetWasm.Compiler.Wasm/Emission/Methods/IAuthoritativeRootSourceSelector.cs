using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IAuthoritativeRootSourceSelector
{
    ImmutableHashSet<RootSource> Select(MethodRootMap roots, ValueFrameLayout values,
        FilterEnvironmentLayout filters, ArrayMetadataCachingPlan arrays);
}
