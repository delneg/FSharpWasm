using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ControlFlowCorpusTemplateTests
{
    [Fact]
    public void CreateRendersSelectedBranchLoopAndTransfer()
    {
        var subject = new ControlFlowCorpusTemplate();

        var fixture = subject.Create("ControlCase", Values(
            ("branch", "short-circuit"), ("loop", "do"), ("transfer", "continue")));

        Assert.Contains("do {", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("&&", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("continue;", fixture.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("__", fixture.Source, StringComparison.Ordinal);
    }

    private static ImmutableDictionary<string, string> Values(
        params (string Name, string Value)[] values) => values
        .ToImmutableDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
}
