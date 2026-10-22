namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LocalReductionPassTests
{
    [Fact]
    public void RemovesOnlyAnUnusedTrailingLocal()
    {
        var original = GeneratedCilReductionTestPrograms.StraightLine();
        var candidate = Assert.Single(new LocalReductionPass().Generate(original));

        Assert.Empty(candidate.Program.Locals);
    }
}
