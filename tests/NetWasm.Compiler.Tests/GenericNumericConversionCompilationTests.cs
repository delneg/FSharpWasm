using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class GenericNumericConversionCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void IntegralSourceConversionsPreserveNativeSaturationAndRejectDestinationOwnedTargets(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Numerics;

            namespace IntegralGenericConversions;

            public interface IConversionProbe<TSelf> : INumberBase<TSelf>
                where TSelf : INumberBase<TSelf>
            {
                public static bool Rejects<TSource, TTarget>(TSource value)
                    where TSource : INumberBase<TSource>
                    where TTarget : INumberBase<TTarget> =>
                    !TSource.TryConvertToChecked(value, out TTarget checkedResult) &&
                    !TSource.TryConvertToSaturating(value, out TTarget saturatingResult) &&
                    !TSource.TryConvertToTruncating(value, out TTarget truncatingResult);

                public static TTarget Saturating<TSource, TTarget>(TSource value)
                    where TSource : INumberBase<TSource>
                    where TTarget : INumberBase<TTarget> =>
                    TSource.TryConvertToSaturating(value, out TTarget result)
                        ? result : throw new NotSupportedException();
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var nativeMaximum = IntPtr.Size == 4 ? nuint.MaxValue : unchecked((nuint)long.MaxValue);
                    return IConversionProbe<long>.Rejects<long, Int128>(42L) &&
                        IConversionProbe<long>.Rejects<long, double>(42L) &&
                        IConversionProbe<ulong>.Rejects<ulong, UInt128>(42UL) &&
                        IConversionProbe<ulong>.Rejects<ulong, ulong>(42UL) &&
                        IConversionProbe<long>.Saturating<long, nuint>(-1) == 0 &&
                        IConversionProbe<long>.Saturating<long, nuint>(0) == 0 &&
                        IConversionProbe<long>.Saturating<long, nuint>(1) == 1 &&
                        IConversionProbe<long>.Saturating<long, nuint>(long.MaxValue) == nativeMaximum &&
                        IConversionProbe<ulong>.Saturating<ulong, nint>(ulong.MaxValue) == nint.MaxValue &&
                        IConversionProbe<ulong>.Saturating<ulong, nint>(1UL) == 1 &&
                        IConversionProbe<long>.Saturating<long, decimal>(42L) == 42M ? 42 : -1;
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("IntegralGenericConversions", source)
            : assets.CompileSource("IntegralGenericConversions", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "IntegralGenericConversions.EntryPoint",
            "Run",
            [],
            target));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void FloatingSourceConversionRejectsTargetsOwnedByTheDestination(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Numerics;

            namespace GenericNumericConversions;

            public interface IConversionProbe<TSelf> : INumberBase<TSelf>
                where TSelf : INumberBase<TSelf>
            {
                public static bool Rejects<TSource, TTarget>(TSource value)
                    where TSource : INumberBase<TSource>
                    where TTarget : INumberBase<TTarget> =>
                    !TSource.TryConvertToChecked(value, out TTarget checkedResult) &&
                    !TSource.TryConvertToSaturating(value, out TTarget saturatingResult) &&
                    !TSource.TryConvertToTruncating(value, out TTarget truncatingResult);
            }

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    IConversionProbe<float>.Rejects<float, int>(42F) &&
                    IConversionProbe<float>.Rejects<float, Int128>(42F) &&
                    IConversionProbe<float>.Rejects<float, double>(42F) &&
                    IConversionProbe<double>.Rejects<double, long>(42D) &&
                    IConversionProbe<double>.Rejects<double, Int128>(42D) &&
                    IConversionProbe<double>.Rejects<double, float>(42D) ? 42 : -1;
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("GenericNumericConversions", source)
            : assets.CompileSource("GenericNumericConversions", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericNumericConversions.EntryPoint",
            "Run",
            [],
            target));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
    }
}
