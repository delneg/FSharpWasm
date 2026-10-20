using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.LoopInvariantFields;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal sealed class MethodSpecializerFactory(
    IControlFlowGraphBuilderFactory graphBuilders,
    IControlFlowGraphAnalyzerFactory graphAnalyzers) : IMethodSpecializerFactory
{
    private readonly IControlFlowGraphBuilderFactory _graphBuilders = graphBuilders ??
        throw new ArgumentNullException(nameof(graphBuilders));
    private readonly IControlFlowGraphAnalyzerFactory _graphAnalyzers = graphAnalyzers ??
        throw new ArgumentNullException(nameof(graphAnalyzers));

    public IMethodSpecializer Create(
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        IFieldRepository fields,
        ITypeFinder types,
        ICalledMethodResolver calledMethods,
        IMethodImplementationResolver methodImplementations,
        ITypeRelationshipClassifier relationships,
        IMethodBodyReader methodBodies,
        WasmOptimizationSettings optimizationSettings,
        IRuntimeIntrinsicRegistry intrinsics) =>
        new MethodSpecializer(
        [
            .. optimizationSettings.SwitchBitTests == SwitchBitTestPolicy.Conservative
                ? [new SwitchBitTestRewriter()]
                : Array.Empty<IMethodRewriteRule>(),
            new SharedReturnEpilogueRewriter(),
            new Int32LocalInitializerRewriter(),
            new ValueTypeFieldReadRewriter(),
            new AggregateFieldHoistingRewriter(
                new AggregateFieldHoistingPlanner(
                    _graphBuilders,
                    _graphAnalyzers,
                    new FieldProjectionResolver(
                        methodBodies,
                        calledMethods,
                        fields,
                        typeDefinitions),
                    optimizationSettings.LoopInvariantFieldHoisting)),
            new RectangularArrayCallRewriter(),
            new IntrinsicCallRewriter(calledMethods, symbols),
            new ClosedTypeIsValueTypeCallRewriter(calledMethods, symbols),
            new InterlockedCallRewriter(calledMethods, symbols),
            new ActivatorCallRewriter(
                typeDefinitions,
                methods,
                methodInstances,
                symbols),
            new CreateSpanCallRewriter(
                calledMethods,
                new ReadOnlySpanDataResolver()),
            new InitializeArrayCallRewriter(
                fields,
                calledMethods,
                symbols),
            new DefaultComparerCallRewriter(
                typeDefinitions,
                methods,
                methodInstances,
                types,
                calledMethods,
                symbols,
                relationships),
            new ConstrainedCallRewriter(
                types,
                typeDefinitions,
                methods,
                methodInstances,
                symbols,
                new ConstrainedCallTargetResolver(
                    types,
                    methods,
                    methodImplementations)),
            new ClosedTypeComparisonRewriter(
                _graphBuilders.Create(),
                calledMethods,
                new PureReferenceComparisonClassifier(methodBodies, calledMethods, typeDefinitions, methods, new SharedReturnEpilogueRewriter(), intrinsics),
                new ClosedTypeIdentityComparer(typeDefinitions)),
            new BoxedNullComparisonRewriter(_graphBuilders.Create(), new NullableTypeResolver()),
            new BoxedTypeTestRewriter(_graphBuilders.Create(), new NullableTypeResolver()),
            new ConstantBranchRewriter(_graphBuilders.Create()),
            new UnreachableInstructionRewriter(_graphBuilders.Create()),
        ]);
}
