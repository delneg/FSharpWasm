using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedThrowFunctionAppenderTests
{
    [Fact]
    public void MissingPlanAppendsNothing()
    {
        var functions = new List<WasmFunctionDefinition>();

        CreateAppender().Append(functions, OptionalFunctionIndex.Missing);

        Assert.Empty(functions);
    }

    [Fact]
    public void PresentPlanAppendsOneNonInlineableThrowFunction()
    {
        var functions = new List<WasmFunctionDefinition>();
        var imports = WasmRuntimeImports.CreateCatalog();

        CreateAppender(imports).Append(functions, OptionalFunctionIndex.At(37));

        var function = Assert.Single(functions);
        Assert.Equal("managed.throw", function.Name);
        Assert.Equal(CliValueKind.Void, function.Type.Result);
        Assert.Equal(
            [CliValueKind.ManagedReference],
            function.Type.Parameters.ToArray());
        Assert.True(function.IsNoInlining);
        Assert.False(function.IsAggressiveInlining);
        Assert.Equal(
            [
                0,
                WasmOpcodes.LocalGet, 0,
                WasmOpcodes.Call,
                (byte)imports.Resolve(RuntimeImportSymbol.BeginThrow),
                WasmOpcodes.LocalGet, 0,
                WasmOpcodes.Throw, 0,
                WasmOpcodes.End,
            ],
            function.Body);
    }

    [Fact]
    public void ConstructorAndAppenderRejectMissingDependencies()
    {
        var imports = WasmRuntimeImports.CreateCatalog();

        Assert.Throws<ArgumentNullException>(() =>
            new ManagedThrowFunctionAppender(null!, imports));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedThrowFunctionAppender(
                new GeneratedFunctionWriterFactory(),
                null!));
        Assert.Throws<ArgumentNullException>(() =>
            CreateAppender(imports).Append(null!, OptionalFunctionIndex.At(0)));
    }

    private static ManagedThrowFunctionAppender CreateAppender(
        IRuntimeImportResolver? imports = null) =>
        new ManagedThrowFunctionAppender(
            new GeneratedFunctionWriterFactory(),
            imports ?? WasmRuntimeImports.CreateCatalog());
}
