namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilQualificationWorkloadTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(RandomCilQualificationWorkload.ExhaustiveShardCount,
        RandomCilQualificationWorkload.ExhaustiveShardCount)]
    public void GetCaseCountTreatsEachProfilePairAsOneShardUnit(int shards, int expectedCases)
    {
        Assert.Equal(expectedCases, RandomCilQualificationWorkload.GetCaseCount(shards));
    }

    [Fact]
    public void GetCaseCountWithoutShardCapUsesExhaustivePlannerCount()
    {
        Assert.Equal(
            RandomCilQualificationWorkload.ExhaustiveShardCount,
            RandomCilQualificationWorkload.GetCaseCount(null));
    }
}
