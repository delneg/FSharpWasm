using System;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed class ArrayBoundsCheckEliminator(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IArrayBoundsCheckEliminationPlanner planner,
    bool enabled) : IArrayBoundsCheckEliminator
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IArrayBoundsCheckEliminationPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var clean = body.ArrayBoundsProof is null ? body : body with { ArrayBoundsProof = null };
        if (!enabled) return clean;
        var accesses = _planner.Plan(_stacks.Validate(_graphs.Build(clean)));
        return accesses.IsEmpty ? clean : clean with
        {
            ArrayBoundsProof = new(clean.Instructions, clean.Locals, clean.LocalSignatureTypes, accesses),
        };
    }
}
