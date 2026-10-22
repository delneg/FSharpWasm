using System;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Analysis.Arrays;

namespace NetWasm.Compiler.Analysis;

internal sealed class ManagedMethodOptimizerFactory(
    ITypedStackValidatorFactory stackValidators,
    IControlFlowGraphBuilderFactory graphBuilders) : IManagedMethodOptimizerFactory
{
    private readonly ITypedStackValidatorFactory _stackValidators = stackValidators ??
        throw new ArgumentNullException(nameof(stackValidators));
    private readonly IControlFlowGraphBuilderFactory _graphBuilders = graphBuilders ??
        throw new ArgumentNullException(nameof(graphBuilders));

    public IManagedMethodOptimizer Create(
        IMethodBodyReader methodBodies,
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodSpecializer specializer,
        ICalledMethodResolver calledMethods,
        IRuntimeIntrinsicRegistry intrinsics,
        IMethodSpecializer constantCallFolding,
        IDispatchTargetResolver dispatchTargets,
        IFinalizerResolver finalizers,
        WasmOptimizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(methodBodies);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(specializer);
        ArgumentNullException.ThrowIfNull(calledMethods);
        ArgumentNullException.ThrowIfNull(intrinsics);
        ArgumentNullException.ThrowIfNull(constantCallFolding);
        ArgumentNullException.ThrowIfNull(dispatchTargets);
        ArgumentNullException.ThrowIfNull(finalizers);
        ArgumentNullException.ThrowIfNull(settings);
        var graphBuilder = _graphBuilders.Create();
        var stackValidator = _stackValidators.Create(types, fields, methods);
        var staticDataViews = new StaticDataViewEliminator(
            calledMethods,
            new StaticDataViewResolver(
                methodBodies,
                specializer,
                types,
                methods),
            settings.StaticDataViewElimination);
        var dominance = new ControlFlowDominanceAnalyzer();
        var induction = new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance);
        return new ManagedMethodOptimizationPipeline(
            staticDataViews,
            constantCallFolding,
            new ManagedMethodInliner(
                new ManagedInliningResolver(
                    methodBodies,
                    specializer,
                    calledMethods,
                    new ManagedInliningBodyValidator(
                        graphBuilder,
                        stackValidator),
                    settings.ManagedMethodInlining,
                    new MethodInitializationTriggerClassifier(types),
                    intrinsics,
                    ManagedInliningResolver.SmallBodyInstructionLimit,
                    ManagedInliningResolver.LeafBodyInstructionLimit),
                new TerminalContinuationInliningPlanner()),
            graphBuilder,
            stackValidator,
            new SimpleValueTypeAccessorCanonicalizer(
                methodBodies,
                calledMethods,
                fields,
                settings.AddressTakenValuePromotion,
                new MethodInitializationTriggerClassifier(types)),
            new ArrayBoundsCheckEliminator(graphBuilder, stackValidator,
                new ArrayBoundsCheckEliminationPlanner(
                    induction, dominance),
                settings.ArrayBoundsCheckElimination == ArrayBoundsCheckEliminationPolicy.Conservative),
            new ArrayLoopUnroller(graphBuilder, stackValidator,
                // Bound expanded CIL work, not a presumed native cache capacity.
                new ArrayLoopUnrollingPlanner(induction, expandedBodyBudget: 128),
                settings.LoopUnrolling == LoopUnrollingPolicy.Bounded),
            new RepeatedArrayAccessEliminator(graphBuilder, stackValidator,
                new RepeatedArrayAccessPlanner(dominance),
                settings.ArrayBoundsCheckElimination == ArrayBoundsCheckEliminationPolicy.Conservative),
            new Devirtualization.ExactReceiverDevirtualizer(calledMethods, dispatchTargets, graphBuilder, stackValidator,
                dominance, settings.Devirtualization == DevirtualizationPolicy.ExactType),
            new Allocations.UnusedAllocationEliminator(methodBodies, calledMethods, finalizers,
                settings.AllocationElimination == AllocationEliminationPolicy.Unused));
    }
}
