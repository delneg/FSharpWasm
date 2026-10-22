namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GeneratedCilReductionScoreTests
{
    [Fact]
    public void OrdersFewerInputsInstructionsLocalsAndSimplerConstantsLower()
    {
        var original = GeneratedCilReductionTestPrograms.StraightLine();
        var score = GeneratedCilReductionScore.Measure(original);

        Assert.True(GeneratedCilReductionScore.Measure(original with
        {
            Inputs = original.Inputs.RemoveAt(0),
        }).CompareTo(score) < 0);
        Assert.True(GeneratedCilReductionScore.Measure(
            new InstructionReductionPass().Generate(original).First())
            .CompareTo(score) < 0);
        Assert.True(GeneratedCilReductionScore.Measure(
            new LocalReductionPass().Generate(original).Single())
            .CompareTo(score) < 0);
        Assert.True(GeneratedCilReductionScore.Measure(
            new ConstantReductionPass().Generate(original).First())
            .CompareTo(score) < 0);
    }
}
