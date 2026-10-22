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

        CreateAppender().Append(
            functions,
            OptionalFunctionIndex.Missing,
            OptionalFunctionIndex.Missing);

        Assert.Empty(functions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresentPlansAppendNonInlineableThrowFunctionsInIndexOrder(bool rethrow)
    {
        var functions = new List<WasmFunctionDefinition>();
        var imports = WasmRuntimeImports.CreateCatalog();

        CreateAppender(imports).Append(
            functions,
            OptionalFunctionIndex.At(37),
            rethrow ? OptionalFunctionIndex.At(38) : OptionalFunctionIndex.Missing);

        Assert.Equal(
            rethrow ? ["managed.throw", "managed.rethrow"] : ["managed.throw"],
            functions.Select(static function => function.Name));
        AssertThrowFunction(functions[0], imports.Resolve(RuntimeImportSymbol.BeginThrow));
        if (rethrow)
        {
            AssertThrowFunction(functions[1], imports.Resolve(RuntimeImportSymbol.BeginRethrow));
        }
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
            CreateAppender(imports).Append(
                null!,
                OptionalFunctionIndex.At(0),
                OptionalFunctionIndex.Missing));
    }

    private static void AssertThrowFunction(WasmFunctionDefinition function, int beginImport)
    {
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
                WasmOpcodes.Call, (byte)beginImport,
                WasmOpcodes.LocalGet, 0,
                WasmOpcodes.Throw, 0,
                WasmOpcodes.End,
            ],
            function.Body);
    }

    private static ManagedThrowFunctionAppender CreateAppender(
        IRuntimeImportResolver? imports = null) =>
        new ManagedThrowFunctionAppender(
            new GeneratedFunctionWriterFactory(),
            imports ?? WasmRuntimeImports.CreateCatalog());
}
