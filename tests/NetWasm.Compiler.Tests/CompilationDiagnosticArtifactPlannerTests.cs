using System.Text.Json;

using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler.Tests;

public sealed class CompilationDiagnosticArtifactPlannerTests
{
    [Fact]
    public void ExplicitOmissionDoesNotProduceExceptionDiagnosticArtifacts()
    {
        using var context = CompilerInvariantTestContext.Create(
            options => options with { EmitExceptionTypeMap = false });

        Assert.Null(context.Result.DiagnosticArtifacts);
    }

    [Fact]
    public void SuccessfulCompilationAutomaticallyBindsReachableExceptionMapToFinalModule()
    {
        using var context = CompilerInvariantTestContext.Create();

        var artifacts = Assert.IsType<DiagnosticArtifactBundle>(context.Result.DiagnosticArtifacts);
        Assert.NotSame(context.Result.ApplicationModule, artifacts.Wasm);
        Assert.Equal(context.Result.ApplicationModule, artifacts.Wasm);
        Assert.Equal(artifacts.ExceptionTypeMap.Sha256, artifacts.Manifest.ExceptionTypeMapSha256);
        Assert.NotEmpty(artifacts.Manifest.WasmSha256);
        using var map = JsonDocument.Parse(artifacts.ExceptionTypeMap.Bytes);
        var entries = map.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.True(entry.GetProperty("typeId").GetInt32() > 0);
            Assert.Contains("Version=", entry.GetProperty("assemblyIdentity").GetString(), StringComparison.Ordinal);
            Assert.Contains("Culture=", entry.GetProperty("assemblyIdentity").GetString(), StringComparison.Ordinal);
            Assert.Contains("PublicKeyToken=", entry.GetProperty("assemblyIdentity").GetString(), StringComparison.Ordinal);
        });
    }
}
