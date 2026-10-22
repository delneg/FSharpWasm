using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.TestInfrastructure;
using Xunit;

namespace NetWasm.Compiler.Tests;

public sealed class StackTraceArtifactTests
{
    [Fact]
    public void WriterProducesDeterministicOrderedSidecar()
    {
        var writer = CreateWriter();

        var artifact = writer.Write([
            new(7, "Example.Worker.Run()"),
            new(1, "Example.Program.Main()"),
        ]);

        Assert.Equal(
            "application/vnd.netwasm.stack-trace-symbols+json;version=1",
            artifact.MediaType);
        Assert.Equal("application.netwasm.stacktrace.json", artifact.FileNameHint);
        Assert.Equal(64, artifact.Sha256.Length);
        using var document = JsonDocument.Parse(artifact.Bytes);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var methods = document.RootElement.GetProperty("methods").EnumerateArray()
            .ToArray();
        Assert.Equal([1, 7], methods.Select(method =>
            method.GetProperty("id").GetInt32()));
        Assert.Equal([
            "Example.Program.Main()",
            "Example.Worker.Run()",
        ], methods.Select(method => method.GetProperty("name").GetString()));
        Assert.Equal(artifact.Bytes, writer.Write([
            new(1, "Example.Program.Main()"),
            new(7, "Example.Worker.Run()"),
        ]).Bytes);
    }

    [Fact]
    public void WriterRejectsUninitializedSymbolArray()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateWriter().Write(default));
    }

    [Fact]
    public void BinderIncludesSidecarOnlyWhenInstrumentationIsEnabled()
    {
        var binder = CreateBinder();
        var result = EmptyResult();
        var disabled = binder.Bind(
            result,
            default,
            Options(emitStackTrace: false));
        Assert.Null(disabled.StackTraceSymbols);

        var enabled = binder.Bind(
            result,
            [new(1, "Example.Program.Main()")],
            Options(emitStackTrace: true));
        Assert.NotNull(enabled.StackTraceSymbols);
    }

    [Fact]
    public void BinderRejectsMissingInputs()
    {
        var binder = CreateBinder();
        Assert.Throws<ArgumentNullException>(() =>
            binder.Bind(null!, [], Options(false)));
        Assert.Throws<ArgumentNullException>(() =>
            binder.Bind(EmptyResult(), [], null!));
    }

    [Fact]
    public void InstrumentedCompilerEmbedsReachableSymbolsAndWritesSidecar()
    {
        using var assets = TestAssets.Create();
        const string identity = "UniqueTraceFixture.EntryPoint::UniqueTraceMethod";
        var assembly = assets.CompileSource(
            "UniqueTraceFixture",
            """
            namespace UniqueTraceFixture;

            public static class EntryPoint
            {
                public static int UniqueTraceMethod(int value) => value;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UniqueTraceFixture.EntryPoint",
            "UniqueTraceMethod",
            [],
            EmitStackTrace: true));

        var sidecar = Assert.IsType<StackTraceSymbolArtifact>(result.StackTraceSymbols);
        Assert.Contains(identity, Encoding.UTF8.GetString(sidecar.Bytes),
            StringComparison.Ordinal);
        Assert.Equal(-1, result.ApplicationModule.AsSpan().IndexOf(
            Encoding.UTF8.GetBytes(identity)));
        Assert.True(result.ApplicationModule.AsSpan().IndexOf(
            Encoding.Unicode.GetBytes(identity)) >= 0);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void PortablePdbUsesSafeDocumentIdentities()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "SafeTraceFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    if (value == 0)
                    {
            #line 42 "/private/developer/work/Secret.cs"
                        return value;
                    }
            #line 73 "/_/src/Mapped.cs"
                    return value + 1;
            #line default
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true));

        var json = Encoding.UTF8.GetString(
            Assert.IsType<StackTraceSymbolArtifact>(result.StackTraceSymbols).Bytes);
        Assert.Contains("Secret.cs:line 42", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/private/developer/work", json, StringComparison.Ordinal);
        Assert.Contains("/_/src/Mapped.cs:line 73", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/File.cs", null, "src/File.cs")]
    [InlineData("C:/work/developer/File.cs", null, "File.cs")]
    [InlineData("//server/share/File.cs", null, "File.cs")]
    [InlineData("/mapped", "source=/mapped", "/mapped")]
    [InlineData("/private/source/File.cs", "missing-equals,source=/other", "File.cs")]
    [InlineData("a", null, "a")]
    [InlineData("123", null, "123")]
    [InlineData("abc", null, "abc")]
    [InlineData("a:x", null, "a:x")]
    public void SourceDocumentIdentityFormattingIsPortable(
        string document,
        string? pathMap,
        string expected)
    {
        var formatter = new SourceDocumentIdentityFormatter();

        var identity = formatter.Format(
            document,
            Options(false) with { PathMap = pathMap });

        Assert.Equal(expected, identity);
    }

    [Fact]
    public void SourceDocumentIdentityFormattingHandlesProjectParentsAndHostPolicy()
    {
        var formatter = new SourceDocumentIdentityFormatter();
        var options = Options(false) with
        {
            ProjectDirectory = "/mapped/project",
            PathMap = "source=/",
        };

        Assert.Equal("mapped", formatter.Format("/mapped", options));
        Assert.Equal(
            "File.cs",
            formatter.Format("/mapped/other/File.cs", options));
        Assert.True(SourceDocumentIdentityFormatter.IsForeignWindowsPath(
            "C:/source/File.cs",
            isWindows: false));
        Assert.False(SourceDocumentIdentityFormatter.IsForeignWindowsPath(
            "C:/source/File.cs",
            isWindows: true));
    }

    [Fact]
    [Trait("Issue", "64")]
    public void PortablePdbDoesNotTreatForeignWindowsPathAsProjectRelative()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "ForeignPathTraceFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 12 "C:\\work\\developer\\Secret.cs"
                    return value;
            #line default
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true,
            ProjectDirectory: assets.Directory));

        var json = Encoding.UTF8.GetString(
            Assert.IsType<StackTraceSymbolArtifact>(result.StackTraceSymbols).Bytes);
        Assert.Contains("Secret.cs:line 12", json, StringComparison.Ordinal);
        Assert.DoesNotContain("work/developer", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void PortablePdbPreservesConfiguredPathMapDestination()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "ConfiguredPathMapTraceFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 23 "/mapped/src/Program.cs"
                    return value;
            #line default
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true,
            ProjectDirectory: assets.Directory,
            PathMap: assets.Directory + "=/mapped"));

        var json = Encoding.UTF8.GetString(
            Assert.IsType<StackTraceSymbolArtifact>(result.StackTraceSymbols).Bytes);
        Assert.Contains("/mapped/src/Program.cs:line 23", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void MalformedPortablePdbFallsBackToMethodSymbols()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "MalformedTracePdb",
            "public static class EntryPoint { public static int Run(int value) => value; }");
        File.WriteAllText(Path.ChangeExtension(assembly, ".pdb"), "not a portable PDB");

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true));

        var json = Encoding.UTF8.GetString(
            Assert.IsType<StackTraceSymbolArtifact>(result.StackTraceSymbols).Bytes);
        Assert.Contains("EntryPoint::Run", json, StringComparison.Ordinal);
        Assert.DoesNotContain(":line ", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void MalformedSourceLinkRootFallsBackToPdbDocument()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdbAndSourceLink(
            "MalformedSourceLinkFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 31 "Safe/Program.cs"
                    return value;
            #line default
                }
            }
            """,
            "[]");

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true));

        var json = Encoding.UTF8.GetString(
            Assert.IsType<StackTraceSymbolArtifact>(result.StackTraceSymbols).Bytes);
        Assert.Contains("EntryPoint::Run", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void PortablePdbPreservesSourceLinkDocumentIdentity()
    {
        using var assets = TestAssets.Create();
        var sourceRoot = assets.Directory.Replace('\\', '/').TrimEnd('/');
        var sourceLink = JsonSerializer.Serialize(new
        {
            documents = new Dictionary<string, string>
            {
                [sourceRoot + "/*"] =
                    "https://raw.githubusercontent.com/example/repository/commit/*",
            },
        });
        var document = sourceRoot + "/Mapped.cs";
        var assembly = assets.CompileSourceWithPortablePdbAndSourceLink(
            "SourceLinkTraceFixture",
            $$"""
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 17 "{{document}}"
                    return value;
            #line default
                }
            }
            """,
            sourceLink);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            EmitStackTrace: true));

        var json = Encoding.UTF8.GetString(
            Assert.IsType<StackTraceSymbolArtifact>(result.StackTraceSymbols).Bytes);
        Assert.Contains(
            "https://raw.githubusercontent.com/example/repository/commit/Mapped.cs:line 17",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(sourceRoot, json, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceLocationReaderRejectsMissingInputsAndSkipsDisabledTracing()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StackTraceSourceLocationReader(null!));
        var reader = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter());

        Assert.Throws<ArgumentNullException>(() => reader.Read(null!, Options(true)));
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DisabledTraceFixture",
            "public static class EntryPoint { public static void Run() { } }");
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        Assert.Throws<ArgumentNullException>(() => reader.Read(metadata.Snapshot, null!));
        Assert.Empty(reader.Read(metadata.Snapshot, Options(false)));
    }

    [Fact]
    public void MissingPortablePdbLeavesSourceLocationsEmpty()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "MissingTracePdb",
            "public static class EntryPoint { public static int Run(int value) => value; }");
        File.Delete(Path.ChangeExtension(assembly, ".pdb"));
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);

        var locations = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter()).Read(
                metadata.Snapshot,
                Options(true));

        Assert.DoesNotContain(
            locations.Keys,
            key => key.Assembly == metadata.Snapshot.EntryAssemblyIdentity);
    }

    [Fact]
    public void OptionalSourceMetadataFailuresLeaveMethodOnlyFrames()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "OptionalTraceMetadata",
            "public static class EntryPoint { public static int Run(int value) => value; }");
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        Exception[] failures =
        [
            new BadImageFormatException(),
            new IOException(),
            new UnauthorizedAccessException(),
            new JsonException(),
            new ArgumentException(),
        ];

        foreach (var failure in failures)
        {
            var locations = new StackTraceSourceLocationReader(
                new ThrowingDocumentFormatter(failure)).Read(
                    metadata.Snapshot,
                    Options(true));
            Assert.Empty(locations);
        }
    }

    [Fact]
    public void PortablePdbUsesExactSourceLinkDocumentMapping()
    {
        using var assets = TestAssets.Create();
        var source = assets.Directory.Replace('\\', '/').TrimEnd('/') + "/Exact.cs";
        const string expected =
            "https://raw.githubusercontent.com/example/repository/commit/Exact.cs";
        var sourceLink = JsonSerializer.Serialize(new
        {
            documents = new Dictionary<string, string>
            {
                ["/different/Exact.cs"] = "https://example.invalid/different.cs",
                [source] = expected,
            },
        });
        var assembly = assets.CompileSourceWithPortablePdbAndSourceLink(
            "ExactSourceLinkFixture",
            $$"""
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 19 "{{source}}"
                    return value;
            #line default
                }
            }
            """,
            sourceLink);
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);

        var locations = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter()).Read(
                metadata.Snapshot,
                Options(true));

        Assert.Contains(
            locations.Values.SelectMany(static value => value),
            location => location.Document == expected && location.StartLine == 19);
    }

    [Fact]
    public void PortablePdbRetainsHiddenSequencePointOffsets()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "HiddenSequencePointFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line hidden
                    value++;
            #line default
                    return value;
                }
            }
            """);
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);

        var locations = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter()).Read(
                metadata.Snapshot,
                Options(true));

        Assert.Contains(
            locations.Values.SelectMany(static value => value),
            location => location.Document is null && location.StartLine == 0);
    }

    [Fact]
    public void InvalidSourceLinkEntriesAndNonMatchingPatternsAreIgnored()
    {
        using var assets = TestAssets.Create();
        const string sourceLink =
            """
            {
              "documents": {
                "number": 42,
                "empty": "",
                "two*wildcards*": "https://example.invalid/two/*",
                "wrong-prefix/*": "https://example.invalid/prefix/*",
                "abc*tail": "https://example.invalid/suffix/*",
                "abc*bcd": "https://example.invalid/overlap/*",
                "different": "https://example.invalid/exact.cs"
              }
            }
            """;
        var assembly = assets.CompileSourceWithPortablePdbAndSourceLink(
            "IgnoredSourceLinkFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int value)
                {
            #line 27 "abcd"
                    return value;
            #line default
                }
            }
            """,
            sourceLink);
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);

        var locations = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter()).Read(
                metadata.Snapshot,
                Options(true));

        Assert.Contains(
            locations.Values.SelectMany(static value => value),
            location => location.Document == "abcd" && location.StartLine == 27);
        Assert.DoesNotContain(
            locations.Values.SelectMany(static value => value),
            location => location.Document?.StartsWith(
                "https://example.invalid/",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SourceLocationReaderHandlesOptionalPdbDocumentPolicies()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "SourceLocationPolicyFixture",
            "public static class EntryPoint { public static void Run() { } }");
        using var stream = File.OpenRead(Path.ChangeExtension(assembly, ".pdb"));
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var metadata = provider.GetMetadataReader();
        var document = Assert.Single(metadata.Documents);
        var reader = new StackTraceSourceLocationReader(
            new SourceDocumentIdentityFormatter());

        Assert.True(StackTraceSourceLocationReader.HasMethodDebugInformation(1, 1));
        Assert.False(StackTraceSourceLocationReader.HasMethodDebugInformation(2, 1));
        Assert.Equal(
            document,
            StackTraceSourceLocationReader.SelectDocument(default, document));
        Assert.Equal(
            document,
            StackTraceSourceLocationReader.SelectDocument(document, default));
        Assert.Null(StackTraceSourceLocationReader.ReadDocument(metadata, default));
        var documentName = Assert.IsType<string>(
            StackTraceSourceLocationReader.ReadDocument(metadata, document));

        var noDocument = reader.CreateLocation(
            3,
            27,
            null,
            [],
            Options(true));
        var blankDocument = reader.CreateLocation(
            3,
            27,
            " ",
            [],
            Options(true));
        var sourceDocument = reader.CreateLocation(
            3,
            27,
            documentName,
            [],
            Options(true));

        Assert.Null(noDocument.Document);
        Assert.Equal(0, noDocument.StartLine);
        Assert.Null(blankDocument.Document);
        Assert.Equal(0, blankDocument.StartLine);
        Assert.NotNull(sourceDocument.Document);
        Assert.Equal(27, sourceDocument.StartLine);
    }

    private static readonly Func<IStackTraceSymbolWriter> CreateWriter =
        static () => new StackTraceSymbolWriter(new ArtifactDigestCalculator());

    private static readonly Func<ICompilationStackTraceArtifactBinder> CreateBinder =
        static () => new CompilationStackTraceArtifactBinder(CreateWriter());

    private static CompilationResult EmptyResult() =>
        new([], null!, null!, null!, 0);

    private static CompilerOptions Options(bool emitStackTrace) =>
        new(
            "application.dll",
            [],
            "Example.Program",
            "Main",
            [],
            EmitStackTrace: emitStackTrace);

    private sealed class ThrowingDocumentFormatter(Exception failure) :
        ISourceDocumentIdentityFormatter
    {
        public string Format(string document, CompilerOptions options) =>
            throw failure;
    }
}
