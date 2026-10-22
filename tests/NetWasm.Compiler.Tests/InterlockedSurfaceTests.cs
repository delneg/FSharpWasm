using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.TestInfrastructure;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class InterlockedSurfaceTests
{
    [Fact]
    public void DeclaresStandard21SurfaceAndIntegerBitwiseOperations()
    {
        using var assets = TestAssets.Create();
        var compilation = CSharpCompilation.Create("InterlockedSurface",
            references: [MetadataReference.CreateFromFile(assets.CoreLib)]);
        var type = compilation.GetTypeByMetadataName("System.Threading.Interlocked");
        Assert.NotNull(type);
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary &&
                method.DeclaredAccessibility == Accessibility.Public).ToArray();
        Assert.Equal(ExpectedMembers.Order(), methods.Select(method => method.GetDocumentationCommentId()).Order());
        Assert.All(methods, method =>
        {
            Assert.True(method.IsStatic);
            Assert.Equal(Accessibility.Public, method.DeclaredAccessibility);
            if (method.Parameters.Length == 0)
            {
                Assert.True(method.ReturnsVoid);
                return;
            }
            Assert.Equal(RefKind.Ref, method.Parameters[0].RefKind);
            Assert.True(SymbolEqualityComparer.Default.Equals(method.ReturnType, method.Parameters[0].Type));
            if (method.IsGenericMethod)
                Assert.True(Assert.Single(method.TypeParameters).HasReferenceTypeConstraint);
        });
    }

    [Fact]
    public void IncrementDoesNotRetainUnusedInterlockedOperations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("InterlockedRetention", """
            using System.Threading;
            public static class EntryPoint
            {
                public static int Run(int input) => Interlocked.Increment(ref input);
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", []));
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method)
            .Where(method => method.DeclaringType.FullName == "System.Threading.Interlocked");
        Assert.Equal(IncrementMembers,
            methods.Select(method => method.Definition.Name).Distinct().Order());
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    private static readonly string[] IncrementMembers = ["Add", "Increment", "ValidateLocation"];

    // Frozen from NETStandard.Library.Ref 2.1.0; And/Or are explicitly newer additions.
    private static readonly string[] ExpectedMembers =
    [
        "M:System.Threading.Interlocked.Add(System.Int32@,System.Int32)",
        "M:System.Threading.Interlocked.Add(System.Int64@,System.Int64)",
        "M:System.Threading.Interlocked.And(System.Int32@,System.Int32)",
        "M:System.Threading.Interlocked.And(System.Int64@,System.Int64)",
        "M:System.Threading.Interlocked.And(System.UInt32@,System.UInt32)",
        "M:System.Threading.Interlocked.And(System.UInt64@,System.UInt64)",
        "M:System.Threading.Interlocked.CompareExchange(System.Double@,System.Double,System.Double)",
        "M:System.Threading.Interlocked.CompareExchange(System.Int32@,System.Int32,System.Int32)",
        "M:System.Threading.Interlocked.CompareExchange(System.Int64@,System.Int64,System.Int64)",
        "M:System.Threading.Interlocked.CompareExchange(System.IntPtr@,System.IntPtr,System.IntPtr)",
        "M:System.Threading.Interlocked.CompareExchange(System.Object@,System.Object,System.Object)",
        "M:System.Threading.Interlocked.CompareExchange(System.Single@,System.Single,System.Single)",
        "M:System.Threading.Interlocked.CompareExchange``1(``0@,``0,``0)",
        "M:System.Threading.Interlocked.Decrement(System.Int32@)",
        "M:System.Threading.Interlocked.Decrement(System.Int64@)",
        "M:System.Threading.Interlocked.Exchange(System.Double@,System.Double)",
        "M:System.Threading.Interlocked.Exchange(System.Int32@,System.Int32)",
        "M:System.Threading.Interlocked.Exchange(System.Int64@,System.Int64)",
        "M:System.Threading.Interlocked.Exchange(System.IntPtr@,System.IntPtr)",
        "M:System.Threading.Interlocked.Exchange(System.Object@,System.Object)",
        "M:System.Threading.Interlocked.Exchange(System.Single@,System.Single)",
        "M:System.Threading.Interlocked.Exchange``1(``0@,``0)",
        "M:System.Threading.Interlocked.Increment(System.Int32@)",
        "M:System.Threading.Interlocked.Increment(System.Int64@)",
        "M:System.Threading.Interlocked.MemoryBarrier",
        "M:System.Threading.Interlocked.MemoryBarrierProcessWide",
        "M:System.Threading.Interlocked.Or(System.Int32@,System.Int32)",
        "M:System.Threading.Interlocked.Or(System.Int64@,System.Int64)",
        "M:System.Threading.Interlocked.Or(System.UInt32@,System.UInt32)",
        "M:System.Threading.Interlocked.Or(System.UInt64@,System.UInt64)",
        "M:System.Threading.Interlocked.Read(System.Int64@)",
    ];
}
