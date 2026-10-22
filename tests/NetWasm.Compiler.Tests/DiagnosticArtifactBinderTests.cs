using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticArtifactBinderTests
{
    [Fact]
    public void ConstructorRejectsMissingCollaborators()
    {
        var digests = new ArtifactDigestCalculator();
        var integrity = new ExceptionTypeMapIntegrityVerifier(digests);
        var manifests = new DiagnosticArtifactManifestWriter();

        Assert.Throws<ArgumentNullException>(() => new DiagnosticArtifactBinder(
            null!, integrity, manifests));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticArtifactBinder(
            digests, null!, manifests));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticArtifactBinder(
            digests, integrity, null!));
    }

    [Fact]
    public void RebindsManifestToExactCallerSuppliedFinalWasm()
    {
        var map = CreateWriter().Write(
        [
            new(1, "Example.Error, Example", "Example.Error", "Error"),
        ], "semantic-input");
        var buildId = new DiagnosticArtifactIdentityCalculator().CalculateBuildId(["semantic-input"]);
        var binder = CreateBinder();
        var preTransform = binder.Bind([0, 97, 115, 109], map, buildId);
        var finalWasm = new byte[] { 0, 97, 115, 109, 1 };

        var rebound = binder.Bind(finalWasm, map, buildId);

        Assert.NotSame(finalWasm, rebound.Wasm);
        Assert.Equal(finalWasm, rebound.Wasm);
        Assert.NotEqual(preTransform.Manifest.WasmSha256, rebound.Manifest.WasmSha256);
        Assert.Equal(buildId, rebound.Manifest.BuildId);
        Assert.Equal(map.Sha256, rebound.Manifest.ExceptionTypeMapSha256);
    }

    [Fact]
    public void SnapshotsPayloadsAndRejectsAStaleMapDigest()
    {
        var map = CreateWriter().Write(
        [
            new(1, "Example.Error, Example", "Example.Error", "Error"),
        ], "semantic-input");
        var finalWasm = new byte[] { 0, 97, 115, 109 };
        var bundle = CreateBinder().Bind(finalWasm, map, "build");

        finalWasm[0] = 1;
        map.Bytes[0] ^= 1;

        Assert.Equal(0, bundle.Wasm[0]);
        Assert.NotEqual(map.Bytes[0], bundle.ExceptionTypeMap.Bytes[0]);
        Assert.Throws<InvalidOperationException>(() =>
            CreateBinder().Bind(finalWasm, map, "build"));
    }

    [Fact]
    public void BindsFinalWasmAndMapWithoutCircularBuildIdentity()
    {
        var map = CreateWriter().Write(
        [
            new(1, "E, A", "E", "A"),
        ], "build");
        var buildId = new DiagnosticArtifactIdentityCalculator().CalculateBuildId(["target=wasm32", "input=abc"]);

        var bundle = CreateBinder().Bind([0, 97, 115, 109], map, buildId);

        Assert.Equal(buildId, bundle.Manifest.BuildId);
        Assert.Equal(map.Sha256, bundle.Manifest.ExceptionTypeMapSha256);
        Assert.NotEmpty(bundle.Manifest.WasmSha256);
        Assert.NotEmpty(bundle.ManifestBytes);
    }

    private static ExceptionTypeMapWriter CreateWriter() =>
        new(new ArtifactDigestCalculator());

    private static DiagnosticArtifactBinder CreateBinder()
    {
        var digests = new ArtifactDigestCalculator();
        return new(
            digests,
            new ExceptionTypeMapIntegrityVerifier(digests),
            new DiagnosticArtifactManifestWriter());
    }

}
