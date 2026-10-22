using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ExceptionTransferCorpusTemplateTests
{
    [Fact]
    public void CreateRendersSelectedRegionTransferAndReturnShape()
    {
        var subject = new ExceptionTransferCorpusTemplate();

        var fixture = subject.Create("ExceptionCase", Values(
            ("region", "filter"), ("transfer", "return"), ("return", "struct")));

        Assert.Contains("when (", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("return Finish", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("readonly struct Result", fixture.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("__", fixture.Source, StringComparison.Ordinal);
    }

    private static ImmutableDictionary<string, string> Values(
        params (string Name, string Value)[] values) => values
        .ToImmutableDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
}
