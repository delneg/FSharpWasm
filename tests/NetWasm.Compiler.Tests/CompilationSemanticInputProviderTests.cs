using System.Collections.Immutable;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class CompilationSemanticInputProviderTests
{
    [Fact]
    public void LibraryAndExecutableModesHaveDifferentSemanticIdentities()
    {
        var options = new CompilerOptions("entry.dll", [], "", "", []);
        var provider = new[] { new CompilationSemanticInputProvider(new RecordingInputHasher()) }
            .Cast<ICompilationSemanticInputProvider>().Single();

        var executable = provider.GetSemanticInputs(options, CreateSnapshot(), []).ToArray();
        var library = provider.GetSemanticInputs(options with
        {
            EntryPointKind = CompilerEntryPointKind.Library,
        }, CreateSnapshot(), []).ToArray();
        var javaScriptWorker = provider.GetSemanticInputs(options with
        {
            EntryPointKind = CompilerEntryPointKind.Library,
            UseJavaScriptExportBoundary = true,
        }, CreateSnapshot(), []).ToArray();

        Assert.Contains("entry-kind=RawFunction", executable);
        Assert.Contains("entry-kind=Library", library);
        Assert.Contains("javascript-export-boundary=True", javaScriptWorker);
        Assert.NotEqual(executable, library);
        Assert.NotEqual(library, javaScriptWorker);
    }

    [Fact]
    public void IncludesOptionalAliasesWorldAndExportsAndValidatesContracts()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationSemanticInputProvider(null!));

        var options = new CompilerOptions(
            "entry.dll",
            ["z.dll", "a.dll"],
            "Program",
            "Main",
            [new RequestedExport("run", "Program", "Run")],
            WitPath: "world.wit",
            WitWorld: "world",
            ReferenceAssemblyAliases: new Dictionary<string, string>
            {
                ["z.dll"] = "z",
                ["a.dll"] = "a",
            }.ToImmutableDictionary());
        var provider = new CompilationSemanticInputProvider(
            new RecordingInputHasher());

        var inputs = provider.GetSemanticInputs(
            options,
            CreateSnapshot(),
            [new ReachableExceptionType(1, "A, Assembly", "A", "Assembly")])
            .ToArray();

        Assert.Contains(inputs, input =>
            input.StartsWith("export=", StringComparison.Ordinal) &&
            input.Contains("run", StringComparison.Ordinal));
        Assert.Contains("alias=a.dll=a", inputs);
        Assert.Contains("alias=z.dll=z", inputs);
        Assert.Contains("wit-world=world", inputs);
        Assert.Throws<ArgumentNullException>(() => provider.GetSemanticInputs(
            null!, CreateSnapshot(), []));
        Assert.Throws<ArgumentNullException>(() => provider.GetSemanticInputs(
            options, null!, []));
        Assert.Throws<ArgumentNullException>(() => provider.GetSemanticInputs(
            options, CreateSnapshot(), null!));
    }

    [Fact]
    public void SemanticInputsUseStableHashesAndSortReachableTypes()
    {
        var options = new CompilerOptions(
            "entry.dll",
            ["reference.dll"],
            "Program",
            "Main",
            [],
            WitPath: "world.wit");
        var types = new[]
        {
            new ReachableExceptionType(2, "B, Assembly", "B", "Assembly"),
            new ReachableExceptionType(1, "A, Assembly", "A", "Assembly"),
        };
        var provider = new CompilationSemanticInputProvider(
            new RecordingInputHasher());

        var first = provider.GetSemanticInputs(
            options,
            CreateSnapshot(),
            types).ToArray();
        var second = provider.GetSemanticInputs(
            options,
            CreateSnapshot(),
            types.Reverse()).ToArray();

        Assert.Equal(first, second);
        Assert.Equal(
            ["exception=1:A, Assembly", "exception=2:B, Assembly"],
            first.Where(input => input.StartsWith("exception=", StringComparison.Ordinal)));
    }

    private sealed class RecordingInputHasher : ICompilationInputHasher
    {
        public string Hash(string path) => path switch
        {
            "world.wit" => "wit-hash",
            _ => "assembly-hash",
        };
    }

    private static MetadataCompilationSnapshot CreateSnapshot() =>
        new([], new AssemblyIdentity("Application"), [], [], []);
}
