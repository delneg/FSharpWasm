using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticScopeReaderTests
{
    [Fact]
    public void ReadReturnsTheCurrentScope()
    {
        var scope = new CompilerDiagnosticScope("compiler.jsonl");
        var state = new CompilerDiagnosticScopeState
        {
            Current =
            {
                Value = new CompilerDiagnosticScopeFrame(scope, Parent: null),
            },
        };
        var reader = Assert.IsAssignableFrom<ICompilerDiagnosticScopeReader>(
            new CompilerDiagnosticScopeReader(state));

        Assert.Same(scope, reader.Read());
    }

    [Fact]
    public void ReadReturnsNullWithoutAFrame()
    {
        var reader = Assert.IsAssignableFrom<ICompilerDiagnosticScopeReader>(
            new CompilerDiagnosticScopeReader(new CompilerDiagnosticScopeState()));

        Assert.Null(reader.Read());
    }
}
