using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class VirtualDispatchEmissionPlanBuilder :
    IVirtualDispatchEmissionPlanBuilder
{
    private const string ObjectCanonicalName = "primitive:object";

    public VirtualDispatchEmissionPlan Build(
        MethodInstanceModel declaration,
        ImmutableArray<DispatchTargetModel> targets)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        if (declaration.Definition.IsAbstract ||
            declaration.DeclaringType.CanonicalName != ObjectCanonicalName)
        {
            return new(targets, null);
        }

        DispatchTargetModel? fallback = null;
        var checkedTargets = ImmutableArray.CreateBuilder<DispatchTargetModel>(
            targets.Length);
        foreach (var target in targets)
        {
            if (target.Method.CanonicalName == declaration.CanonicalName)
            {
                fallback ??= target;
                continue;
            }

            checkedTargets.Add(target);
        }

        return fallback is null
            ? new(targets, null)
            : new(checkedTargets.ToImmutable(), fallback);
    }
}
