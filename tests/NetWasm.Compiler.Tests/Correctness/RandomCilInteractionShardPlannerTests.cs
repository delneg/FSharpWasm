namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilInteractionShardPlannerTests
{
    [Fact]
    public void ShardsExhaustiveMatrixWithoutLossOrDuplication()
    {
        var matrix = new RandomCilInteractionMatrix();
        var planner = new RandomCilInteractionShardPlanner(matrix);

        var shards = planner.CreateShards();
        var cases = shards.SelectMany(shard => shard.Cases).ToArray();

        Assert.Equal(RandomCilQualificationWorkload.ExhaustiveShardCount, shards.Length);
        Assert.All(shards[..^1], shard =>
            Assert.Equal(RandomCilInteractionShardPlanner.DefaultCasesPerShard,
                shard.Cases.Length));
        Assert.Equal(120, shards[^1].Cases.Length);
        Assert.Equal(RandomCilQualificationWorkload.ExhaustiveProgramCount, cases.Length);
        Assert.Equal(cases.Length, cases.Select(testCase => testCase.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, shards.Length), shards.Select(shard => shard.Index));
    }
}
