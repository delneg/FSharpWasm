using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class MathPowAccuracyCompilationTests
{
    // Each row is (x, y, expected result) as IEEE bits. The rows are exact powers and special
    // values, where Emscripten's size-optimized pow (the fdlibm e_pow.c that Math.Pow ports), musl's
    // pow, and desktop .NET 10 on Linux (glibc) and macOS all return the same bits. Inexact results
    // are excluded because fdlibm's pow is nearly rounded rather than correctly rounded.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void PowReturnsExactPowersAndSpecialValues(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"MathPowAccuracy_{optimize}_{target}", Source, "MathPowAccuracy.EntryPoint", optimize, target, 41, []));

        // A negative result identifies the first mismatching row: -1 - row for Math.Pow,
        // -1001 - row for MathF.Pow.
        Assert.Equal(42, observed);
    }

    private const string Source = """
        using System;

        namespace MathPowAccuracy;

        public static class EntryPoint
        {
            private static readonly ulong[] Doubles =
            [
                0x4008000000000000UL, 0x4000000000000000UL, 0x4022000000000000UL,
                0x4000000000000000UL, 0x4024000000000000UL, 0x4090000000000000UL,
                0x4024000000000000UL, 0x402e000000000000UL, 0x430c6bf526340000UL,
                0x4024000000000000UL, 0x4036000000000000UL, 0x4480f0cf064dd592UL,
                0xc008000000000000UL, 0x4008000000000000UL, 0xc03b000000000000UL,
                0xc008000000000000UL, 0x4010000000000000UL, 0x4054400000000000UL,
                0xc000000000000000UL, 0x4008000000000000UL, 0xc020000000000000UL,
                0x3ff8000000000000UL, 0x4008000000000000UL, 0x400b000000000000UL,
                0x401c000000000000UL, 0x4008000000000000UL, 0x4075700000000000UL,
                0x4008000000000000UL, 0x4040800000000000UL, 0x4333bfefa65abb83UL,
                0x4030000000000000UL, 0x3fd0000000000000UL, 0x4000000000000000UL,
                0x3fe0000000000000UL, 0xc024000000000000UL, 0x4090000000000000UL,
                0xbfe0000000000000UL, 0xc008000000000000UL, 0xc020000000000000UL,
                0x4022000000000000UL, 0x3fe0000000000000UL, 0x4008000000000000UL,
                0x4010000000000000UL, 0xbff8000000000000UL, 0x3fc0000000000000UL,
                0x4000000000000000UL, 0x408ff80000000000UL, 0x7fe0000000000000UL,
                0x4000000000000000UL, 0xc08ff00000000000UL, 0x0010000000000000UL,
                0x4000000000000000UL, 0xc090c80000000000UL, 0x0000000000000001UL,
                0x3fe0000000000000UL, 0x4090c80000000000UL, 0x0000000000000001UL,
                0xc000000000000000UL, 0xc090c80000000000UL, 0x0000000000000001UL,
                0x0000000000000001UL, 0x3fe0000000000000UL, 0x1e60000000000000UL,
                0x4000000000000000UL, 0x3fe0000000000000UL, 0x3ff6a09e667f3bcdUL,
                0x01a56e1fc2f8f359UL, 0x3fe0000000000000UL, 0x20ca2fe76a3f9475UL,
                0xc000000000000000UL, 0x4090040000000000UL, 0xfff0000000000000UL,
                0x4000000000000000UL, 0x4090000000000000UL, 0x7ff0000000000000UL,
                0x3feccccccccccccdUL, 0x4202a05f20000000UL, 0x0000000000000000UL,
                0x4000000000000000UL, 0xc090cc0000000000UL, 0x0000000000000000UL,
                0x0000000000000000UL, 0xbff0000000000000UL, 0x7ff0000000000000UL,
                0x8000000000000000UL, 0xc008000000000000UL, 0xfff0000000000000UL,
                0x8000000000000000UL, 0x4008000000000000UL, 0x8000000000000000UL,
                0x8000000000000000UL, 0x4000000000000000UL, 0x0000000000000000UL,
                0xfff0000000000000UL, 0x4008000000000000UL, 0xfff0000000000000UL,
                0xfff0000000000000UL, 0xc008000000000000UL, 0x8000000000000000UL,
                0x7ff0000000000000UL, 0xbfe0000000000000UL, 0x0000000000000000UL,
                0x3fe0000000000000UL, 0x7ff0000000000000UL, 0x0000000000000000UL,
                0x4000000000000000UL, 0xfff0000000000000UL, 0x0000000000000000UL,
                0xbff0000000000000UL, 0x7ff0000000000000UL, 0x3ff0000000000000UL,
                0x7ff8000000000000UL, 0x0000000000000000UL, 0x3ff0000000000000UL,
                0x3ff0000000000000UL, 0x7ff8000000000000UL, 0x3ff0000000000000UL,
                0xc020000000000000UL, 0x3ff0000000000000UL, 0xc020000000000000UL,
                0xc020000000000000UL, 0xbff0000000000000UL, 0xbfc0000000000000UL,
            ];

            private static readonly uint[] Singles =
            [
                0x40400000U, 0x40000000U, 0x41100000U,
                0xc0000000U, 0x40400000U, 0xc1000000U,
                0x40000000U, 0xc3150000U, 0x00000001U,
                0xbf000000U, 0xc0a00000U, 0xc2000000U,
                0x41200000U, 0x41200000U, 0x501502f9U,
                0x3e800000U, 0xc0400000U, 0x42800000U,
                0x3fc00000U, 0x40000000U, 0x40100000U,
                0x40800000U, 0x3f000000U, 0x40000000U,
                0x40000000U, 0x3f000000U, 0x3fb504f3U,
                0x00000000U, 0xbf800000U, 0x7f800000U,
                0x80000000U, 0xc0400000U, 0xff800000U,
                0xff800000U, 0x40400000U, 0xff800000U,
                0x40000000U, 0x43000000U, 0x7f800000U,
                0x3f000000U, 0x43160000U, 0x00000000U,
            ];

            public static int Run(int input)
            {
                for (var row = 0; row < Doubles.Length / 3; row++)
                {
                    var result = Math.Pow(
                        BitConverter.UInt64BitsToDouble(Doubles[3 * row]),
                        BitConverter.UInt64BitsToDouble(Doubles[3 * row + 1]));
                    if (BitConverter.DoubleToUInt64Bits(result) != Doubles[3 * row + 2]) return -1 - row;
                }
                for (var row = 0; row < Singles.Length / 3; row++)
                {
                    var result = MathF.Pow(
                        BitConverter.UInt32BitsToSingle(Singles[3 * row]),
                        BitConverter.UInt32BitsToSingle(Singles[3 * row + 1]));
                    if (BitConverter.SingleToUInt32Bits(result) != Singles[3 * row + 2]) return -1001 - row;
                }
                return input + 1;
            }
        }
        """;
}
