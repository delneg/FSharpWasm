using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeCollectorCompatibilityValidatorTests
{
    private readonly RuntimeCollectorCompatibilityValidator _validator = new();

    [Fact]
    public void AcceptsTracingCollectorsAndLifetimeFreeBumpLayouts()
    {
        _validator.Validate(RuntimePackTestData.Target("wasm32"), LegacyLayout());
        _validator.Validate(RuntimePackTestData.Target("wasm32", "Compact"), Layout("finalization"));
        _validator.Validate(RuntimePackTestData.Target("wasm32", "NetWasm"), Layout("finalization"));
        _validator.Validate(RuntimePackTestData.Target("wasm32", "Bump"), Layout());
        _validator.Validate(RuntimePackTestData.Target("wasm32", "Bump"),
            Layout("structured-command-diagnostics"));
    }

    [Theory]
    [InlineData("ephemeron-handles")]
    [InlineData("finalization")]
    [InlineData("weak-handles")]
    public void RejectsBumpLifetimeFeatures(string feature)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _validator.Validate(RuntimePackTestData.Target("wasm32", "Bump"), Layout(feature)));
        Assert.Contains(feature, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsLegacyBumpLayoutWithoutFeatureEvidence()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _validator.Validate(RuntimePackTestData.Target("wasm32", "Bump"), LegacyLayout()));
        Assert.Contains("runtime-feature evidence", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, Layout()));
        Assert.Throws<ArgumentNullException>(() =>
            _validator.Validate(RuntimePackTestData.Target("wasm32", "Bump"), null!));
    }

    private static RuntimeLayout Layout(params string[] features) => new(3, "wasm32", 0)
    {
        RuntimeFeatures = [.. features],
    };

    private static RuntimeLayout LegacyLayout() => new(2, "wasm32", 0);
}
