using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NativeCallbackCompilationTests
{
    [Fact]
    public void CompilerUnifiesNamedAndAddressTakenCallbackReachability()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "NamedAddressedCallbackFixture",
            """
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            namespace NamedAddressedCallbackFixture;

            public static unsafe class EntryPoint
            {
                [UnmanagedCallersOnly(
                    EntryPoint = "named_callback",
                    CallConvs = [typeof(CallConvCdecl)])]
                private static int Callback(int value) => value + 1;

                public static int Run(int value)
                {
                    delegate* unmanaged[Cdecl]<int, int> callback = &Callback;
                    return callback == null ? 0 : value;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NamedAddressedCallbackFixture.EntryPoint",
            "Run",
            [],
            Target: WasmTarget.Wasm32));

        var support = Assert.IsType<WasmNativeCallbackSupportArtifact>(
            result.NativeCallbackSupport);
        var callback = Assert.Single(support.Callbacks);
        Assert.Equal("named_callback", callback.NativeSymbol);
        Assert.Equal("named_callback", callback.ApplicationExportName);
        Assert.NotNull(callback.RuntimeGetterExportName);
    }
}
