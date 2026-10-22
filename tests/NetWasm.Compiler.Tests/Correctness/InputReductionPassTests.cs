namespace NetWasm.Compiler.Tests.Correctness;

public sealed class InputReductionPassTests
{
    [Fact]
    public void RemovesOneInputPerCandidate()
    {
        var original = GeneratedCilReductionTestPrograms.StraightLine();
        var candidates = new InputReductionPass().Generate(original).ToArray();

        Assert.Equal(original.Inputs.Length, candidates.Length);
        Assert.All(candidates, candidate =>
            Assert.Equal(original.Inputs.Length - 1, candidate.Inputs.Length));
    }
}
