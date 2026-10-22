using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.Tests;

public sealed class ArrayBoundsProofTests
{
    [Fact]
    public void ProvesNonNullForCheckedAndNonNullAccessesOfTheBodyItDescribes()
    {
        ImmutableArray<CilInstruction> instructions = [new(0, 1, CilOperation.Nop, new CilOperand.None())];
        ImmutableArray<CliValueKind> locals = [CliValueKind.I4];
        ImmutableArray<CliTypeIdentity> types = [CliTypeIdentity.FromStackKind(CliValueKind.I4)];
        var proof = new ArrayBoundsProof(instructions, locals, types, [1]) { NonNullOffsets = [2] };

        // A checked access also passed its null check.
        Assert.True(proof.ProvesNonNull(1));
        Assert.True(proof.ProvesNonNull(2));
        Assert.False(proof.ProvesNonNull(3));
        Assert.True(proof.Describes(instructions, locals, types));
        Assert.False(proof.Describes([.. instructions], locals, types));
        Assert.False(proof.Describes(instructions, [.. locals], types));
        Assert.False(proof.Describes(instructions, locals, [.. types]));
    }
}
