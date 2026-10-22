namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class DelegateInvocationGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void CustomDelegateCallsPreserveCallerReferencesAcrossCollections() =>
        runner.Run(new("DelegateInvocationRoots", "DelegateInvocationRoots", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.Finalization,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace DelegateInvocationRoots;

        public delegate void Callback();
        public sealed class Witness
        {
            public static int Finalized;
            public int Value;
            ~Witness() => Finalized++;
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Collect()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Invoke(Callback callback) => callback();

            public static int Run(int input)
            {
                Callback callback = Collect;
                var witness = new Witness { Value = input };
                object first = input;
                callback();
                if (Witness.Finalized != 0 || witness.Value != input || (int)first != input) return 1;
                object second = input + 1;
                Invoke(callback);
                if (Witness.Finalized != 0 || witness.Value != input || (int)second != input + 1) return 2;
                return input;
            }
        }
        """;
}
