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
        WasmOptimizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(methodBodies);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(specializer);
        ArgumentNullException.ThrowIfNull(calledMethods);
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
            new ManagedMethodInliner(
                new ManagedInliningResolver(
                    methodBodies,
                    specializer,
                    calledMethods,
                    new ManagedInliningBodyValidator(
                        graphBuilder,
                        stackValidator),
                    settings.ManagedMethodInlining,
                    new MethodInitializationTriggerClassifier(types)),
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
                settings.LoopUnrolling == LoopUnrollingPolicy.Bounded));
    }
}
