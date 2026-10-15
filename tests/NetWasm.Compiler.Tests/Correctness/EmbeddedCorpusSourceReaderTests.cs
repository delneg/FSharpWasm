using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class EmbeddedCorpusSourceReaderTests
{
    private readonly ICorpusSourceReader _reader =
        Assert.IsAssignableFrom<ICorpusSourceReader>(new EmbeddedCorpusSourceReader());

    [Fact]
    public void ReadsTheExactVersionedSourceWithoutPathDependentPreprocessing()
    {
        var source = _reader.Read("math-boundary/Program.cs.txt");

        Assert.Equal(
            "c8a03d8d48b646ff3f4157d2819fd8dcf91fb40c666df6e9e3c1614c52ca7f0c",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
        Assert.Equal(source, _reader.Read("math-boundary/Program.cs.txt"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void RejectsAnEmptyResourceIdentity(string? sourceFile) =>
        Assert.ThrowsAny<ArgumentException>(() => _reader.Read(sourceFile!));

    [Theory]
    [InlineData("missing/Program.cs.txt")]
    [InlineData("Math-boundary/Program.cs.txt")]
    [InlineData("math-boundary\\Program.cs.txt")]
    [InlineData("../math-boundary/Program.cs.txt")]
    [InlineData("/math-boundary/Program.cs.txt")]
    public void MissingOrNonExactIdentityCannotFallBackToAnotherSource(string sourceFile)
    {
        var error = Assert.Throws<FileNotFoundException>(() => _reader.Read(sourceFile));

        Assert.Equal(sourceFile, error.FileName);
    }
}
