using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler.Tests;

public sealed class CompilationInputHasherTests
{
    [Fact]
    public void HashesAFileAndRejectsMissingInputs()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-input-hasher-");
        try
        {
            var path = Path.Combine(directory.FullName, "input.dll");
            File.WriteAllBytes(path, [1, 2, 3]);
            var hasher = new CompilationInputHasher();

            Assert.NotEmpty(hasher.Hash(path));
            var error = Assert.Throws<FileNotFoundException>(() =>
                hasher.Hash(Path.Combine(directory.FullName, "missing.dll")));
            Assert.Contains("missing.dll", error.FileName!, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void HashesDirectoryContentsIndependentOfCreationOrder()
    {
        var first = Directory.CreateTempSubdirectory("netwasm-input-hasher-a-");
        var second = Directory.CreateTempSubdirectory("netwasm-input-hasher-b-");
        try
        {
            File.WriteAllText(Path.Combine(first.FullName, "b.wit"), "package test:b;");
            File.WriteAllText(Path.Combine(first.FullName, "a.wit"), "package test:a;");
            File.WriteAllText(Path.Combine(second.FullName, "a.wit"), "package test:a;");
            File.WriteAllText(Path.Combine(second.FullName, "b.wit"), "package test:b;");
            var hasher = new CompilationInputHasher();

            Assert.Equal(hasher.Hash(first.FullName), hasher.Hash(second.FullName));
        }
        finally
        {
            first.Delete(recursive: true);
            second.Delete(recursive: true);
        }
    }
}
