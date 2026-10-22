using System.Text.Json;

using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler.Tests;

public sealed class ExceptionTypeMapWriterTests
{
    [Fact]
    public void WriterAndIntegrityVerifierRejectMissingCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() => new ExceptionTypeMapWriter(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ExceptionTypeMapIntegrityVerifier(null!));
    }

    [Fact]
    public void PlannerAndWriterProduceDeterministicSchemaVersionTwoMap()
    {
        var types = new[]
        {
            new ReachableExceptionType(7, "B, App, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "B", "App, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"),
            new ReachableExceptionType(2, "A, App, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "A", "App, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"),
        };

        var validator = new ReachableExceptionTypeValidator();
        var writer = new ExceptionTypeMapWriter(new ArtifactDigestCalculator());
        var first = writer.Write(validator.Validate(types), "build");
        var second = writer.Write(types.Reverse(), "build");

        Assert.Equal(first.Bytes, second.Bytes);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal("application/vnd.netwasm.exception-types+json;version=2", first.MediaType);
        Assert.Equal("application.netwasm.exceptions.json", first.FileNameHint);
        using var document = JsonDocument.Parse(first.Bytes);
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("build", document.RootElement.GetProperty("buildId").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("entries")[0].GetProperty("typeId").GetInt32());
    }

}
