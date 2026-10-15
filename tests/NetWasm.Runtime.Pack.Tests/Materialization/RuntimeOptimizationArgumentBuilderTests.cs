using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeOptimizationArgumentBuilderTests
{
    [Fact]
    public void RejectsMissingPolicy() =>
        Assert.Throws<ArgumentNullException>(() => new RuntimeOptimizationArgumentBuilder(null!));

    [Fact]
    public void UsesResolvedFlagRatherThanItsOwnPresetTable()
    {
        var policy = new FixedPolicy();
        var builder = Assert.IsAssignableFrom<IRuntimeOptimizationArgumentBuilder>(new RuntimeOptimizationArgumentBuilder(policy));
        var arguments = builder.Build(new(RuntimePackTestData.Target("wasm32"), "/runtime.wasm", RuntimeWasmOptimization.Oz));

        Assert.Equal(RuntimeWasmOptimization.Oz, policy.Mode);
        Assert.Equal("-O2", Assert.Single(arguments, IsOptimizationFlag));
    }

    private sealed class FixedPolicy : IRuntimeOptimizationPolicyResolver
    {
        public RuntimeWasmOptimization? Mode { get; private set; }

        public RuntimeOptimizationPolicy Resolve(RuntimeWasmOptimization optimization)
        {
            Mode = optimization;
            return new("-O2", false);
        }
    }

    [Theory]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O0, "-O0")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O1, "-O1")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O2, "-O2")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O3, "-O3")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.Os, "-Os")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.Oz, "-Oz")]
    [InlineData("wasm64", true, RuntimeWasmOptimization.Oz, "-Oz")]
    public void ReproducesPinnedEmscriptenOptimizationWithSelectedMode(
        string target,
        bool memory64,
        RuntimeWasmOptimization optimization,
        string optimizationFlag)
    {
        using var directory = new TemporaryDirectory();
        var output = directory.PathTo("runtime.wasm");

        var arguments = new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(new(
            RuntimePackTestData.Target(target), output, optimization)
        {
            RuntimeGlobalBase = 1024,
        });

        Assert.Contains("--post-emscripten", arguments);
        Assert.Equal(optimizationFlag, Assert.Single(arguments, IsOptimizationFlag));
        Assert.Contains("--low-memory-unused", arguments);
        Assert.Contains("--strip-debug", arguments);
        Assert.Contains("--strip-producers", arguments);
        Assert.Equal(2, arguments.Count(argument => argument == Path.GetFullPath(output)));
        Assert.Equal(memory64, arguments.Contains("--enable-memory64"));
    }

    [Fact]
    public void KeepsLowMemoryLiveWhenStaticDataStartsBelowTheBinaryenBoundary()
    {
        using var directory = new TemporaryDirectory();

        var arguments = new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(new(
            RuntimePackTestData.Target("wasm32"),
            directory.PathTo("runtime.wasm"),
            RuntimeWasmOptimization.Oz)
        {
            RuntimeGlobalBase = 1008,
        });

        Assert.DoesNotContain("--low-memory-unused", arguments);
    }

    [Fact]
    public void NoneProducesNoOptimizerArguments()
    {
        using var directory = new TemporaryDirectory();
        Assert.Empty(new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(new(
            RuntimePackTestData.Target("wasm32"), directory.PathTo("runtime.wasm"),
            RuntimeWasmOptimization.None)));
    }

    [Fact]
    public void RejectsUndefinedOptimization() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(new(
                RuntimePackTestData.Target("wasm32"), "runtime.wasm",
                (RuntimeWasmOptimization)42)));

    [Fact]
    public void RejectsMissingRequest() =>
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(null!));

    private static bool IsOptimizationFlag(string argument) =>
        argument is "-O0" or "-O1" or "-O2" or "-O3" or "-Os" or "-Oz";
}
