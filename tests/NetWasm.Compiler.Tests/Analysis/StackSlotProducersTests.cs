using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class StackSlotProducersTests
{
    private static readonly AssemblyIdentity Assembly = new("ProducerTests");
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Assembly, "", "Owner", false);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);

    [Fact]
    public void FindsAProducerThatConsumesItsArguments()
    {
        var producers = Producers(
            [
                Constant(0), new(1, 2, CilOperation.NewObject, Call(Void, Int32)), Constant(2),
                Store(3, 1), Store(4, 0),
            ],
            [], [CliValueKind.I4], [CliValueKind.ValueType], [CliValueKind.ValueType, CliValueKind.I4], [CliValueKind.ValueType]);

        Assert.Equal(1, producers.FindTop(4));
        Assert.Equal(2, producers.FindTop(3));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void ACallPushesItsResultOrNothing(bool result, int producer)
    {
        var producers = Producers(
            [Constant(0), new(1, 2, CilOperation.Call, Call(result ? Int32 : Void)), Store(2, 0)],
            [], [CliValueKind.I4], result ? [CliValueKind.I4, CliValueKind.I4] : [CliValueKind.I4]);

        Assert.Equal(producer, producers.FindTop(2));
    }

    [Fact]
    public void AnInstructionThatMayPushStopsTheSearchBelowIt()
    {
        var producers = Producers(
            [Constant(0), new(1, 2, CilOperation.Call, new CilOperand.None()), Store(2, 0)],
            [], [CliValueKind.I4], [CliValueKind.I4]);

        Assert.Null(producers.FindTop(2));
    }

    [Fact]
    public void ADuplicateProducesItsCopyAndReadsTheOriginal()
    {
        var producers = Producers(
            [Constant(0), new(1, 2, CilOperation.Duplicate, new CilOperand.None()), Store(2, 0)],
            [], [CliValueKind.I4], [CliValueKind.I4, CliValueKind.I4]);

        Assert.Equal(1, producers.FindTop(2));
        Assert.Null(producers.Find(2, 0));
    }

    [Fact]
    public void AnotherEntryOrAnUnknownStackStopsTheSearch()
    {
        var entered = Producers(
            [Constant(0), Store(1, 0), new(2, 3, CilOperation.Branch, new CilOperand.BranchTarget(1))],
            [], [CliValueKind.I4], []);
        var unknown = new StackSlotProducers(
            Body([Constant(0), Store(1, 0)]),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(1, [CliValueKind.I4]));

        Assert.True(entered.Enters(1));
        Assert.Null(entered.FindTop(1));
        Assert.Null(unknown.FindTop(1));
        Assert.Null(unknown.FindTop(0));
    }

    private static CilInstruction Constant(int offset) => new(offset, offset + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(offset));

    private static CilInstruction Store(int offset, int local) => new(offset, offset + 1, CilOperation.StoreLocal, new CilOperand.Index(local));

    private static CilOperand.MethodInstance Call(CliTypeIdentity result, params CliTypeIdentity[] parameters)
    {
        var signature = MethodSignatureModel.Create(result, parameters);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Callee", true, signature, 1);
        return new CilOperand.MethodInstance(new(definition, Owner, [], signature));
    }

    private static CilMethodBody Body(ImmutableArray<CilInstruction> instructions) => new(
        new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000001), "Caller", true,
            MethodSignatureModel.Create(Void), 1),
        2,
        [CliValueKind.I4, CliValueKind.I4],
        instructions);

    private static StackSlotProducers Producers(
        ImmutableArray<CilInstruction> instructions,
        params ImmutableArray<CliValueKind>[] stacks) => new(
        Body(instructions),
        stacks.Select((stack, offset) => (stack, offset)).ToImmutableDictionary(pair => pair.offset, pair => pair.stack));
}
