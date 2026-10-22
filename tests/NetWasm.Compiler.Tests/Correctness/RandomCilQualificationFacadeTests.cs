using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilQualificationFacadeTests
{
    [Fact]
    public void AcceptsEveryExpectedSuccessfulPairedShardResult()
    {
        var results = ImmutableArray.Create(
            new RandomCilCampaignResult("shard-0000", TimeSpan.Zero, null));

        RandomCilQualificationFacade.ValidateResults(results, maximumShards: 1);
    }

    [Fact]
    public void RejectsFailFastPartialResultsAndFailures()
    {
        var partial = ImmutableArray<RandomCilCampaignResult>.Empty;
        var failed = ImmutableArray.Create(
            new RandomCilCampaignResult("shard-0000", TimeSpan.Zero, new InvalidOperationException()));

        Assert.Throws<AggregateException>(
            () => RandomCilQualificationFacade.ValidateResults(partial, maximumShards: 1));
        Assert.Throws<AggregateException>(
            () => RandomCilQualificationFacade.ValidateResults(failed, maximumShards: 1));
    }
}
