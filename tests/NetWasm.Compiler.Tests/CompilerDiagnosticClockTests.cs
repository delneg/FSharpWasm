using System;

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticClockTests
{
    [Fact]
    public void ReadReturnsTheCurrentUtcTime()
    {
        var clock = Assert.IsAssignableFrom<ICompilerDiagnosticClock>(new CompilerDiagnosticClock());
        var before = DateTimeOffset.UtcNow;

        var value = clock.Read();

        var after = DateTimeOffset.UtcNow;
        Assert.InRange(value, before, after);
        Assert.Equal(TimeSpan.Zero, value.Offset);
    }
}
