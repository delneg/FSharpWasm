using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeCollectorCompatibilityValidator : IRuntimeCollectorCompatibilityValidator
{
    private static readonly ImmutableHashSet<string> UnsupportedBumpFeatures =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ephemeron-handles",
            "finalization",
            "weak-handles");

    public void Validate(RuntimePackTarget target, RuntimeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(layout);
        if (target.GarbageCollector != RuntimeGarbageCollectors.Bump)
        {
            return;
        }

        if (layout.RuntimeFeatures.IsDefault)
        {
            throw new InvalidOperationException(
                "The Bump collector requires current compiler runtime-feature evidence.");
        }

        var unsupported = layout.RuntimeFeatures.FirstOrDefault(UnsupportedBumpFeatures.Contains);
        if (unsupported is not null)
        {
            throw new InvalidOperationException(
                $"The Bump collector does not support the '{unsupported}' runtime feature.");
        }
    }
}
