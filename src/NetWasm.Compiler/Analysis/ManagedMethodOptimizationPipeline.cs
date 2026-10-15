using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class ManagedMethodOptimizationPipeline(
    IManagedMethodOptimizer staticDataViews,
    IManagedMethodInliner inliner,
    IControlFlowGraphBuilder graphBuilder,
    ITypedStackValidator stackValidator,
    ISimpleValueTypeAccessorCanonicalizer accessors,
    IArrayBoundsCheckEliminator arrayBounds,
    IArrayLoopUnroller unroller) : IManagedMethodOptimizer
{
    private readonly IManagedMethodOptimizer _staticDataViews = staticDataViews ??
        throw new ArgumentNullException(nameof(staticDataViews));
    private readonly IManagedMethodInliner _inliner = inliner ??
        throw new ArgumentNullException(nameof(inliner));
    private readonly IControlFlowGraphBuilder _graphBuilder = graphBuilder ??
        throw new ArgumentNullException(nameof(graphBuilder));
    private readonly ITypedStackValidator _stackValidator = stackValidator ??
        throw new ArgumentNullException(nameof(stackValidator));
    private readonly ISimpleValueTypeAccessorCanonicalizer _accessors = accessors ??
        throw new ArgumentNullException(nameof(accessors));
    private readonly IArrayBoundsCheckEliminator _arrayBounds = arrayBounds ??
        throw new ArgumentNullException(nameof(arrayBounds));
    private readonly IArrayLoopUnroller _unroller = unroller ?? throw new ArgumentNullException(nameof(unroller));

    public CilMethodBody Optimize(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var initial = _staticDataViews.Optimize(method);
        var entryStacks = ReferenceEquals(initial, method.Body)
            ? method.ControlFlow.InstructionEntryStacks
            : ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;
        var inlined = _inliner.Inline(
            method.Method,
            initial,
            entryStacks);
        var optimized = ReferenceEquals(inlined, initial)
            ? inlined
            : _staticDataViews.Optimize(new(
                method.Method,
                _stackValidator.Validate(_graphBuilder.Build(inlined))));
        return _unroller.Rewrite(_arrayBounds.Rewrite(_accessors.Canonicalize(optimized)));
    }
}
