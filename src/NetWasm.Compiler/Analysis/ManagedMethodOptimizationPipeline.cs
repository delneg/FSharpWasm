using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.Analysis.Devirtualization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class ManagedMethodOptimizationPipeline(
    IManagedMethodOptimizer staticDataViews,
    IMethodSpecializer constantCalls,
    IManagedMethodInliner inliner,
    IControlFlowGraphBuilder graphBuilder,
    ITypedStackValidator stackValidator,
    ISimpleValueTypeAccessorCanonicalizer accessors,
    IArrayBoundsCheckEliminator arrayBounds,
    IArrayLoopUnroller unroller,
    IRepeatedArrayAccessEliminator repeatedAccesses,
    IExactReceiverDevirtualizer devirtualizer,
    IMethodSpecializer allocations) : IManagedMethodOptimizer
{
    private readonly IManagedMethodOptimizer _staticDataViews = staticDataViews ??
        throw new ArgumentNullException(nameof(staticDataViews));
    private readonly IMethodSpecializer _constantCalls = constantCalls ??
        throw new ArgumentNullException(nameof(constantCalls));
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
    private readonly IRepeatedArrayAccessEliminator _repeatedAccesses = repeatedAccesses ??
        throw new ArgumentNullException(nameof(repeatedAccesses));
    private readonly IExactReceiverDevirtualizer _devirtualizer = devirtualizer ??
        throw new ArgumentNullException(nameof(devirtualizer));
    private readonly IMethodSpecializer _allocations = allocations ?? throw new ArgumentNullException(nameof(allocations));

    // Inlined callees bring their own calls, so a few rounds reach helpers of
    // helpers. Each round revalidates the body and re-enters static data view
    // elimination, which recognizes producers only while they are still calls.
    internal const int MaximumInliningDepth = 3;

    public CilMethodBody Optimize(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var optimized = _staticDataViews.Optimize(method);
        var entryStacks = ReferenceEquals(optimized, method.Body)
            ? method.ControlFlow.InstructionEntryStacks
            : ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;
        var settled = false;
        for (var depth = 0; depth < MaximumInliningDepth; depth++)
        {
            // A constant argument is spilled to a local once its call is inlined, so fold first.
            var folded = _constantCalls.Rewrite(optimized);
            if (!ReferenceEquals(folded, optimized))
            {
                optimized = folded;
                entryStacks = ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;
            }
            // A direct call keeps the stack shape of the virtual call it replaces.
            optimized = _devirtualizer.Rewrite(optimized, entryStacks);
            var inlined = _inliner.Inline(
                method.Method,
                optimized,
                entryStacks,
                nested: depth > 0);
            if (ReferenceEquals(inlined, optimized))
            {
                settled = true;
                break;
            }

            var validated = _stackValidator.Validate(_graphBuilder.Build(inlined));
            optimized = _staticDataViews.Optimize(new(method.Method, validated));
            entryStacks = ReferenceEquals(optimized, inlined)
                ? validated.InstructionEntryStacks
                : ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;
        }
        // The last round's inlined calls have not been devirtualized yet.
        if (!settled)
        {
            optimized = _devirtualizer.Rewrite(optimized, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);
        }
        // Inlined callees leave receivers and allocations that nothing reads.
        return _repeatedAccesses.Rewrite(
            _unroller.Rewrite(_arrayBounds.Rewrite(_accessors.Canonicalize(_allocations.Rewrite(optimized)))));
    }
}
