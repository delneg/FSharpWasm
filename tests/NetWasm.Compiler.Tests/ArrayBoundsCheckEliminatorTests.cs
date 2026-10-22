using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ArrayBoundsCheckEliminatorTests
{
    [Fact]
    public void RequiresDependenciesAndInputBeforeAnalysis()
    {
        var flow = ArrayLoopInductionAnalyzerTests.Create(0, 1).Flow;
        var collaborators = new RecordingAnalysis(flow, []);
        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminator(null!, collaborators, collaborators, true));
        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminator(collaborators, null!, collaborators, true));
        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminator(collaborators, collaborators, null!, true));
        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminator(collaborators, collaborators, collaborators, true).Rewrite(null!));
        Assert.Empty(collaborators.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledPolicyClearsPreviousProofWithoutAnalysis(bool hadProof)
    {
        var flow = ArrayLoopInductionAnalyzerTests.Create(0, 1).Flow;
        var body = flow.Graph.MethodBody;
        if (hadProof) body = body with
        {
            ArrayBoundsProof = new(body.Instructions, body.Locals, body.LocalSignatureTypes, [7]),
        };
        var collaborators = new RecordingAnalysis(flow, [7]);
        var actor = new ArrayBoundsCheckEliminator(collaborators, collaborators, collaborators, false);
        var result = actor.Rewrite(body);
        Assert.Null(result.ArrayBoundsProof);
        Assert.Empty(collaborators.Events);
        Assert.Equal(body.Instructions, result.Instructions);
        if (!hadProof) Assert.Same(body, result);
    }

    [Fact]
    public void RecomputesProofAfterClearingPreviousFacts()
    {
        var flow = ArrayLoopInductionAnalyzerTests.Create(0, 1).Flow;
        var body = flow.Graph.MethodBody;
        body = body with
        {
            ArrayBoundsProof = new(body.Instructions, body.Locals, body.LocalSignatureTypes, [999]),
        };
        var collaborators = new RecordingAnalysis(flow, [7]);
        var actor = new ArrayBoundsCheckEliminator(collaborators, collaborators, collaborators, true);
        var result = actor.Rewrite(body);
        Assert.Equal(["graph", "validate", "plan"], collaborators.Events);
        Assert.NotNull(result.ArrayBoundsProof);
        Assert.Equal([7], result.ArrayBoundsProof.AccessOffsets.ToArray());
        Assert.Equal(body.Instructions, result.ArrayBoundsProof.Instructions);
        Assert.Equal(body.LocalSignatureTypes, result.ArrayBoundsProof.LocalSignatureTypes);
        Assert.Equal(body.Locals, result.ArrayBoundsProof.Locals);
    }

    [Fact]
    public void EmptyProofPreservesBodyIdentity()
    {
        var flow = ArrayLoopInductionAnalyzerTests.Create(0, 1).Flow;
        var collaborators = new RecordingAnalysis(flow, []);
        var actor = new ArrayBoundsCheckEliminator(collaborators, collaborators, collaborators, true);
        Assert.Same(flow.Graph.MethodBody, actor.Rewrite(flow.Graph.MethodBody));
        Assert.Equal(["graph", "validate", "plan"], collaborators.Events);
    }

    private sealed class RecordingAnalysis(ValidatedControlFlowGraph flow, ImmutableHashSet<int> offsets) :
        IControlFlowGraphBuilder, ITypedStackValidator, IArrayBoundsCheckEliminationPlanner
    {
        public List<string> Events { get; } = [];
        public ControlFlowGraph Build(CilMethodBody method)
        {
            Assert.Null(method.ArrayBoundsProof);
            Events.Add("graph");
            return flow.Graph;
        }
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph)
        {
            Assert.Same(flow.Graph, graph);
            Events.Add("validate");
            return flow;
        }
        public ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow)
        {
            Assert.Same(flow, controlFlow);
            Events.Add("plan");
            return offsets;
        }
    }
}
