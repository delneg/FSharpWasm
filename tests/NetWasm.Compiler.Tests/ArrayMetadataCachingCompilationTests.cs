using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ArrayMetadataCachingCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false, false)]
    [InlineData(WasmTarget.Wasm64, false, false)]
    [InlineData(WasmTarget.Wasm32, true, false)]
    [InlineData(WasmTarget.Wasm64, true, false)]
    [InlineData(WasmTarget.Wasm32, false, true)]
    [InlineData(WasmTarget.Wasm64, false, true)]
    [InlineData(WasmTarget.Wasm32, true, true)]
    [InlineData(WasmTarget.Wasm64, true, true)]
    public void PreservesArrayExceptionsAliasesAndOwnership(WasmTarget target, bool enabled, bool duplication)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayMetadataCaching", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new(
                LoopInvariantFieldHoisting: enabled
                    ? ManagedLoopInvariantFieldHoisting.Enabled : ManagedLoopInvariantFieldHoisting.Disabled,
                BranchPrefixDuplication: duplication
                    ? ManagedBranchPrefixDuplication.Enabled : ManagedBranchPrefixDuplication.Disabled)));
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
                var output = new int[2];
                Copy(null, null, null, 0);
                Copy(new int[] { 2, 3 }, new int[] { 5, 7 }, output, 2);
                if (output[0] != 7 || output[1] != 10) return 1;
                try { Copy(new int[0], null, output, 1); return 2; }
                catch (IndexOutOfRangeException) { }
                try { Copy(null, new int[0], output, 1); return 3; }
                catch (NullReferenceException) { }
                try { Copy(new int[] { 11, 13 }, new int[] { 17 }, output, 2); return 4; }
                catch (IndexOutOfRangeException) { if (output[0] != 28) return 5; }
                if (ReassignAndSuspend() != 54) return 6;
                if (Alias(new int[] { 1, 2, 3 }) != 12) return 7;
                if (Exposed(new int[] { 1 }) != 9) return 8;
                if (Collect(new int[] { 11, 13 }) != 24) return 9;
                try { StoreReference(new string[1]); return 10; }
                catch (ArrayTypeMismatchException) { }
                try { NativeIndex(new int[1], (nint)(-1)); return 11; }
                catch (IndexOutOfRangeException) { }
                if (IntPtr.Size == 8)
                {
                    try { NativeIndex(new int[1], unchecked((nint)0x100000000L)); return 12; }
                    catch (IndexOutOfRangeException) { }
                }
                try { Length(null); return 13; }
                catch (NullReferenceException) { }
                return 42;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Copy(int[] left, int[] right, int[] output, int count)
            {
                for (var i = 0; i < count; i++) output[i] = left[i] + right[i];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReassignAndSuspend()
            {
                var a = new int[] { 17, 23 };
                var b = new int[] { 1, 3 };
                var sum = 0;
                for (var i = 0; i < a.Length; i++) sum += a[i];
                // The outer array operand denotes old a, not the new assigned array.
                sum += a[(a = b)[0]];
                for (var i = 0; i < a.Length; i++) sum -= a[i];
                a = null;
                a = new int[] { -5 };
                for (var i = 0; i < a.Length; i++) sum += a[i];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Alias(int[] values)
            {
                var alias = values;
                var sum = 0;
                for (var i = 0; i < values.Length; i++)
                {
                    alias[i] *= 2;
                    sum += values[i];
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Exposed(int[] values)
            {
                Replace(ref values);
                var sum = 0;
                for (var i = 0; i < values.Length; i++) sum += values[i];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Replace(ref int[] values) => values = new int[] { 4, 5 };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Collect(int[] values)
            {
                var sum = 0;
                for (var i = 0; i < values.Length; i++)
                {
                    GC.Collect();
                    sum += values[i];
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void StoreReference(object[] values)
            {
                for (var i = 0; i < values.Length; i++) values[i] = new object();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NativeIndex(int[] values, nint index)
            {
                var sum = 0;
                for (var i = 0; i < values.Length; i++) sum += values[index];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Length(int[] values)
            {
                var sum = 0;
                for (var i = 0; i < 2; i++) sum += values.Length;
                return sum;
            }
        }
        """;
}
