using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ValueTypeInheritedToStringCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void StructsWithoutToStringNameTheirOwnType(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"ValueTypeInheritedToString_{optimize}_{target}",
            Source,
            "ValueTypeInheritedToStringFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, observed);
    }

    private const string Source = """
        using System;

        namespace ValueTypeInheritedToStringFixture;

        public struct Plain { public int Value; }

        public struct Pair { public long First; public object? Second; }

        public struct Named
        {
            public override string ToString() => "named";
        }

        public enum Color { Red, Green }

        public static class EntryPoint
        {
            static string Describe<T>(T value) where T : struct => value.ToString()!;

            public static int Run(int input)
            {
                // A direct call on a struct that inherits ToString passes its address, not a box.
                if (new Plain { Value = 5 }.ToString() != "ValueTypeInheritedToStringFixture.Plain") return 1;
                var pair = new Pair { First = 7, Second = "x" };
                if (pair.ToString() != "ValueTypeInheritedToStringFixture.Pair") return 2;

                // Generic code reaches the same constrained call.
                if (Describe(new Plain()) != "ValueTypeInheritedToStringFixture.Plain") return 3;
                if (Describe(pair) != "ValueTypeInheritedToStringFixture.Pair") return 4;

                // Boxed calls, overrides, enums and nullable values keep their own paths.
                if (((object)new Plain()).ToString() != "ValueTypeInheritedToStringFixture.Plain") return 5;
                if (new Named().ToString() != "named" || Describe(new Named()) != "named") return 6;
                if (Color.Green.ToString() != "Green" || Describe(Color.Red) != "Red") return 7;
                Plain? present = new Plain(), missing = null;
                if (present.ToString() != "ValueTypeInheritedToStringFixture.Plain" || missing.ToString() != "") return 8;
                return input + 1;
            }
        }
        """;
}
