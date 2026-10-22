using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ArrayLoopUnrollingCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void PreservesTailOverflowAndFloatingOrder(WasmTarget target, bool enabled)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayLoopUnrolling", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new(LoopUnrolling: enabled ? ManagedLoopUnrolling.Enabled : ManagedLoopUnrolling.Disabled)));
        var floating = compilation.Program.Methods.Values.Single(method => method.Method.Definition.Name == "FloatSum");
        Assert.Equal(enabled ? 5 : 1,
            floating.Body.Instructions.Count(instruction => instruction.Operation == CilOperation.LoadArrayElement));
        Assert.Equal(enabled ? 5 : 1, floating.Body.ArrayBoundsProof!.AccessOffsets.Count);
        var integer = compilation.Program.Methods.Values.Single(method => method.Method.Definition.Name == "Sum");
        Assert.Equal(enabled ? 5 : 1, integer.Body.ArrayBoundsProof!.AccessOffsets.Count);
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 0, target, compilation.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        public static class EntryPoint
        {
            public static int Run(int unused)
            {
                for (var count = 0; count < 13; count++)
                    if (Sum(count) != unchecked(count * long.MaxValue - count * (count - 1L) / 2)) return 1;
                if (FloatSum() != 1f || DoubleSum() != 1d) return 2;
                if (PartialWrite() != 17) return 3;
                try { NullSum(); return 4; }
                catch (NullReferenceException) { }
                return 42;
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Sum(int count)
            {
                var values = Create(count);
                var sum = 0L;
                for (var i = 0; i < values.Length; i++) sum = unchecked(sum + values[i]);
                return sum;
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long[] Create(int count)
            {
                var values = new long[count];
                for (var i = 0; i < values.Length; i++) values[i] = long.MaxValue - i;
                return values;
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long[] NullArray() => null;
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long NullSum()
            {
                var values = NullArray();
                var sum = 0L;
                for (var i = 0; i < values.Length; i++) sum += values[i];
                return sum;
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float FloatSum()
            {
                var values = new float[] { 16777216f, 1f, -16777216f, 1f, 16777216f, 1f, -16777216f, 1f };
                var sum = 0f;
                for (var i = 0; i < values.Length; i++) sum += values[i];
                return sum;
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static double DoubleSum()
            {
                var values = new double[] { 9007199254740992d, 1d, -9007199254740992d, 1d,
                    9007199254740992d, 1d, -9007199254740992d, 1d };
                var sum = 0d;
                for (var i = 0; i < values.Length; i++) sum += values[i];
                return sum;
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int PartialWrite()
            {
                var output = new int[8];
                try { Copy(output); }
                catch (IndexOutOfRangeException) { return output[0] + output[1] + output[2]; }
                return -1;
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Copy(int[] output)
            {
                var values = new int[] { 8, 9 };
                var destination = output;
                for (var i = 0; i < destination.Length; i++) destination[i] = values[i];
            }
        }
        """;
}
