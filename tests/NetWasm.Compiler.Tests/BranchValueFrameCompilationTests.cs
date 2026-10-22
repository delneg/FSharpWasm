using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BranchValueFrameCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void PreservesScalarAliasesAcrossColdCallsExceptionsAndSubsequentInvocations(WasmTarget target, bool richExceptions)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"BranchValueFrame_{target}_{richExceptions}",
            """
            using System;
            using System.Runtime.CompilerServices;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    for (int index = 0; index < 32; index++)
                    {
                        if (Evaluate(input, false, false) != 22) return 1;
                        if (Evaluate(input, true, false) != 52) return 2;
                        try
                        {
                            _ = Evaluate(input, true, true);
                            return 3;
                        }
                        catch (InvalidOperationException) { }
                        if (Evaluate(input, true, false) != 52) return 4;
                        if (Evaluate(input, false, false) != 22) return 5;
                    }
                    return 42;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long Evaluate(int input, bool cold, bool fail)
                {
                    long scalar = input + 7;
                    if (cold)
                        Mutate(ref scalar, fail);
                    else
                        scalar += 5;
                    return scalar;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Mutate(ref long value, bool fail)
                {
                    value = value * 3 + 1;
                    if (fail) throw new InvalidOperationException();
                }
            }
            """, "EntryPoint", true, target, 10, [])
        {
            Optimization = FinalWasmOptimization.O3,
            EmitStackTrace = richExceptions,
        });

        Assert.Equal(42, result);
    }
}
