using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.StackTraces;

namespace NetWasm.Compiler.Tests;

public sealed class StackTraceReachabilityRootProviderTests
{
    private static readonly AssemblyIdentity Assembly =
        new("StackTraceRoots");
    private static readonly EntityKey ExceptionKey = new(Assembly, 0x02000001);
    private static readonly EntityKey StringKey = new(Assembly, 0x02000002);
    private static readonly EntityKey StackTraceKey = new(Assembly, 0x04000001);

    [Fact]
    public void DisabledInstrumentationProvidesNoRoots()
    {
        var provider = new StackTraceReachabilityRootProvider();

        var roots = provider.Provide(false, new TypeFinder(), new FieldRepository());

        Assert.Same(NetWasm.Compiler.Analysis.ReachabilityRoots.Empty, roots);
    }

    [Fact]
    public void EnabledInstrumentationProvidesItsTypesAndField()
    {
        var provider = new StackTraceReachabilityRootProvider();

        var roots = provider.Provide(true, new TypeFinder(), new FieldRepository());

        Assert.Equal([ExceptionKey, StringKey], roots.Types.ToArray());
        Assert.Equal([StackTraceKey], roots.Fields.ToArray());
    }

    [Fact]
    public void ProviderRejectsMissingCollaborators()
    {
        var provider = new StackTraceReachabilityRootProvider();

        Assert.Throws<ArgumentNullException>(() =>
            provider.Provide(false, null!, new FieldRepository()));
        Assert.Throws<ArgumentNullException>(() =>
            provider.Provide(false, new TypeFinder(), null!));
    }

    private sealed class TypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => fullName switch
        {
            "System.Exception" => new(
                ExceptionKey,
                "System",
                "Exception",
                false,
                [StackTraceKey],
                []),
            "System.String" => new(
                StringKey,
                "System",
                "String",
                false,
                [],
                []),
            _ => throw new InvalidOperationException(fullName),
        };
    }

    private sealed class FieldRepository : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key)
        {
            Assert.Equal(StackTraceKey, key);
            return new(
                StackTraceKey,
                ExceptionKey,
                "_stackTrace",
                CliTypeIdentity.Named(Assembly, "System", "String", false),
                false);
        }
    }
}
