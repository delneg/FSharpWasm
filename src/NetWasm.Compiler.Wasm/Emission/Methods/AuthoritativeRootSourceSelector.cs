using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class AuthoritativeRootSourceSelector : IAuthoritativeRootSourceSelector
{
    public ImmutableHashSet<RootSource> Select(MethodRootMap roots, ValueFrameLayout values,
        FilterEnvironmentLayout filters, ArrayMetadataCachingPlan arrays)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(arrays);
        return roots.Slots.Keys.Where(source => !source.IsIndirect &&
            source.Kind is RootSourceKind.Argument or RootSourceKind.Local &&
            !filters.Captures.ContainsKey(new(source.Kind == RootSourceKind.Argument, source.Index)) &&
            !arrays.Slots.Contains(new(source.Kind == RootSourceKind.Argument, source.Index)) &&
            (source.Kind == RootSourceKind.Argument
                ? !values.ArgumentOffsets.ContainsKey(source.Index)
                : !values.SpilledScalarLocals.Contains(source.Index))).ToImmutableHashSet();
    }
}
