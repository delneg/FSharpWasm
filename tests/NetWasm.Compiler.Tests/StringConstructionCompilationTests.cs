using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class StringConstructionCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesCharacterArraySliceConstruction(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StringConstructionFixture",
            Source,
            "StringConstructionFixture.EntryPoint",
            optimize,
            target,
            0,
            ImmutableArray<string>.Empty));

        Assert.Equal(31, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesCharacterSpanConstruction(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StringSpanConstructionFixture",
            SpanSource,
            "StringSpanConstructionFixture.EntryPoint",
            optimize,
            target,
            0,
            ImmutableArray<string>.Empty));

        Assert.Equal(31, observed);
    }

    private const string Source = """
        using System;

        namespace StringConstructionFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var characters = new[] { 'a', 'b', 'c', 'd' };
                var observed = new string(characters, 1, 2) == "bc" ? 1 : 0;

                try
                {
                    _ = new string(characters, -1, 1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    observed |= 2;
                }

                try
                {
                    _ = new string(characters, 1, -1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    observed |= 4;
                }

                try
                {
                    _ = new string(characters, 3, 2);
                }
                catch (ArgumentOutOfRangeException)
                {
                    observed |= 8;
                }

                try
                {
                    _ = new string((char[])null!, 0, 0);
                }
                catch (ArgumentNullException)
                {
                    observed |= 16;
                }

                return observed + input - input;
            }
        }
        """;

    private const string SpanSource = """
        using System;

        namespace StringSpanConstructionFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var observed = 0;
                var empty = new string(default(ReadOnlySpan<char>));
                if (empty.Length == 0)
                {
                    observed |= 1;
                }

                var source = new[] { 'x', 'a', '\0', '\ud83d', '\ude00', 'z' };
                var text = new string(new ReadOnlySpan<char>(source, 1, 4));
                source[1] = 'q';
                if (text.Length == 4 && text[0] == 'a')
                {
                    observed |= 2;
                }
                if (text[1] == '\0')
                {
                    observed |= 4;
                }
                if (text[2] == '\ud83d' && text[3] == '\ude00')
                {
                    observed |= 8;
                }

                Span<char> stack = stackalloc char[3];
                stack[0] = 'n';
                stack[1] = 'e';
                stack[2] = 't';
                if (new string(stack) == "net")
                {
                    observed |= 16;
                }

                return observed + input - input;
            }
        }
        """;
}
