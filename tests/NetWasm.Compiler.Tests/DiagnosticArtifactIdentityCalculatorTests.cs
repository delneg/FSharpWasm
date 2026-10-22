using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticArtifactIdentityCalculatorTests
{
    [Fact]
    public void BuildIdentityIsIndependentOfInputEnumerationOrder()
    {
        var calculator = new DiagnosticArtifactIdentityCalculator();
        Assert.Equal(calculator.CalculateBuildId(["b", "a"]), calculator.CalculateBuildId(["a", "b"]));
    }
}
