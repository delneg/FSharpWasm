namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GeneratedCilValidatorTests
{
    [Fact]
    public void RejectsATypeValidFinalStackWithoutAControlFlowTerminator()
    {
        var original = GeneratedCilReductionTestPrograms.StraightLine().Program;
        var block = original.Blocks[0];
        var unterminated = original with
        {
            Blocks = original.Blocks.SetItem(0, block with
            {
                Instructions = block.Instructions.RemoveAt(
                    block.Instructions.Length - 1),
            }),
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            new GeneratedCilValidator().Validate(unterminated));

        Assert.Contains("does not end with a terminator", error.Message);
    }
}
