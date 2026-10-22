using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ManagedAggregateLocalCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PreservesOverlappingLocalLanesAcrossWritingAndReadonlyCalls(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("AggregateLocalSynchronization", """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            [StructLayout(LayoutKind.Explicit, Size = 12)]
            public struct Buffer
            {
                [FieldOffset(0)] public uint A;
                [FieldOffset(4)] public uint B;
                [FieldOffset(8)] public uint C;
                [FieldOffset(0)] public ulong Low;
                [FieldOffset(4)] public ulong High;
            }

            public static class EntryPoint
            {
                public static int Run(int mode)
                {
                    try { return Worker(mode == 0 ? 3 : -1); }
                    catch (InvalidOperationException) { return mode == 1 ? 42 : 2; }
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Worker(int seed)
                {
                    Unsafe.SkipInit(out Buffer value);
                    if (value.A != 0 || value.B != 0 || value.C != 0) return 3;
                    value.Low = 0x1122334455667788UL;
                    value.C = 0x99AABBCC;
                    ref ulong overlap = ref value.High;
                    overlap ^= 0x0102030405060708UL;
                    ulong expectedHigh = 0x99AABBCC11223344UL ^ 0x0102030405060708UL;
                    if (value.High != expectedHigh || value.A != 0x55667788) return 4;
                    if (value.B != (uint)expectedHigh || value.C != (uint)(expectedHigh >> 32)) return 5;
                    uint expectedA = value.A + (uint)seed;
                    uint expectedC = value.C ^ expectedA;
                    Mutate(ref value, ref value, seed);
                    if (value.A != expectedA || value.C != expectedC) return 6;
                    if (Inspect(in value) != ((ulong)expectedC << 32 | (uint)expectedHigh)) return 7;
                    for (int index = 0; index < 3; index++)
                    {
                        value.Low += 0x100000001UL;
                        Mutate(ref value, ref value, index);
                    }
                    return value.High == Inspect(in value) ? 42 : 8;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Mutate(ref Buffer first, ref Buffer second, int seed)
                {
                    first.A += (uint)seed;
                    GC.Collect();
                    second.C ^= first.A;
                    if (seed < 0) throw new InvalidOperationException();
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static ulong Inspect(in Buffer value) => value.High;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3));
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 0, target, compilation.StaticDataEnd));
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 1, target, compilation.StaticDataEnd));
    }
}
