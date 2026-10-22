using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class ConstantBranchRewriterTests
{
    [Fact]
    public void UnknownAssignmentInvalidatesAnEarlierLocalConstant()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(3, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(5, CilOperation.Return));

        Assert.Same(body, Rule(body).Rewrite(body));
    }

    [Fact]
    public void RejectsMissingGraphBuilderAndBody()
    {
        Assert.Throws<ArgumentNullException>(() => new ConstantBranchRewriter(null!));
        var body = Body(Instruction(0, CilOperation.Return));
        Assert.Throws<ArgumentNullException>(() => Rule(body).Rewrite(null!));
    }

    [Theory]
    [InlineData(0, CilOperation.BranchIfTrue, false)]
    [InlineData(1, CilOperation.BranchIfTrue, true)]
    [InlineData(-1, CilOperation.BranchIfTrue, true)]
    [InlineData(0, CilOperation.BranchIfFalse, true)]
    [InlineData(1, CilOperation.BranchIfFalse, false)]
    public void FoldsBlockLocalBooleanCopies(int value, CilOperation branch, bool taken)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(value)),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.Nop),
            Instruction(3, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(4, branch, new CilOperand.BranchTarget(6)),
            Instruction(5, CilOperation.Return),
            Instruction(6, CilOperation.Return));
        var rule = Rule(body);

        var rewritten = rule.Rewrite(body);

        Assert.Equal(CilOperation.StoreLocal, rewritten.Instructions[1].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[3].Operation);
        Assert.Equal(taken ? CilOperation.Branch : CilOperation.Nop, rewritten.Instructions[4].Operation);
        Assert.Equal(body.Instructions[4].SourceOffset, rewritten.Instructions[4].SourceOffset);
    }

    [Theory]
    [InlineData(2, 2, 1)]
    [InlineData(2, 3, 0)]
    public void FoldsConstantEquality(int left, int right, int expected)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(left)),
            Instruction(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(right)),
            Instruction(2, CilOperation.CompareEqual),
            Instruction(3, CilOperation.Return));

        var rewritten = new ConstantBranchRewriter(Graph(body)).Rewrite(body);

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[1].Operation);
        Assert.Equal(new CilOperand.ConstantI4(expected), rewritten.Instructions[2].Operand);
    }

    [Theory]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.LoadLocalAddress)]
    [InlineData(CilOperation.StoreObject)]
    public void ForgetsLocalFactsAcrossPossibleEffects(CilOperation effect)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, effect),
            Instruction(3, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(4, CilOperation.Return));

        Assert.Same(body, new ConstantBranchRewriter(Graph(body)).Rewrite(body));
    }

    internal static IMethodRewriteRule Rule(CilMethodBody body) => new ConstantBranchRewriter(Graph(body));
}
