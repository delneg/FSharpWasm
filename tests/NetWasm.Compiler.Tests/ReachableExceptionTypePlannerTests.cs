using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class ReachableExceptionTypePlannerTests
{
    [Fact]
    public void PlannerReturnsNoEntriesWhenExceptionRootIsAbsent()
    {
        var assembly = new AssemblyIdentity("Example");
        var key = new EntityKey(assembly, 1);
        var type = CliTypeIdentity.Named(
            assembly, "Example", "Failure", isValueType: false);
        var planner = new ReachableExceptionTypePlanner(
            new ReachableExceptionTypeValidator());

        var entries = planner.Plan(
            CreateSnapshot(assembly),
            new RecordingTypeIdentityResolver((key, type)),
            new RecordingAssemblyFormatter(),
            new DescriptorSource(
                [new(key, 1, 0, 12, 0, 0, null)], []));

        Assert.Empty(entries);
    }

    [Fact]
    public void PlannerHandlesUnknownBasesCyclesConstructedTypesAndFallbackNames()
    {
        var assembly = new AssemblyIdentity("Example");
        var rootKey = new EntityKey(assembly, 1);
        var zeroKey = new EntityKey(assembly, 0);
        var unknownKey = new EntityKey(assembly, 2);
        var cycleKey = new EntityKey(assembly, 3);
        var fallbackKey = new EntityKey(assembly, 4);
        var root = CliTypeIdentity.Named(
            assembly, "System", "Exception", isValueType: false);
        var unknown = CliTypeIdentity.Named(
            assembly, "Example", "UnknownBaseFailure", isValueType: false);
        var zero = CliTypeIdentity.Named(
            assembly, "Example", "ZeroFailure", isValueType: false);
        var cycle = CliTypeIdentity.Named(
            assembly, "Example", "CycleFailure", isValueType: false);
        var genericDefinition = CliTypeIdentity.Named(
            assembly, "Example", "GenericFailure`1", isValueType: false);
        var fallback = CliTypeIdentity.GenericInstantiation(
            genericDefinition, [genericDefinition]);
        var constructedIdentity = CliTypeIdentity.GenericInstantiation(
            genericDefinition, [root]);
        var planner = new ReachableExceptionTypePlanner(
            new ReachableExceptionTypeValidator());

        var entries = planner.Plan(
            CreateSnapshot(assembly),
            new RecordingTypeIdentityResolver(
                (rootKey, root),
                (zeroKey, zero),
                (unknownKey, unknown),
                (cycleKey, cycle),
                (fallbackKey, fallback)),
            new RecordingAssemblyFormatter(),
            new DescriptorSource(
                [
                    new(rootKey, 1, 0, 12, 0, 0, null),
                    new(zeroKey, 0, 0, 12, 0, 0, null),
                    new(unknownKey, 2, 999, 12, 0, 0, null),
                    new(cycleKey, 3, 3, 12, 0, 0, null),
                    new(fallbackKey, 4, 1, 12, 0, 0, null),
                ],
                [new(constructedIdentity, 5, 1, 12, 0, 0, null)]));

        Assert.Contains(entries, entry => entry.TypeId == 1);
        Assert.Contains(entries, entry => entry.TypeId == 4);
        Assert.Contains(entries, entry => entry.TypeId == 5);
        Assert.Equal(fallback.CanonicalName, entries.Single(entry => entry.TypeId == 4).DisplayName);
        Assert.DoesNotContain(entries, entry => entry.TypeId is 2 or 3);
    }

    [Fact]
    public void ResolveDeclaringAssemblyRejectsUnrootedConstructedIdentity()
    {
        var parameter = CliTypeIdentity.GenericParameter(method: false, index: 0);
        var malformed = CliTypeIdentity.GenericInstantiation(parameter, [parameter]);

        Assert.Throws<InvalidOperationException>(() =>
            ReachableExceptionTypePlanner.ResolveDeclaringAssembly(malformed));
        Assert.Throws<ArgumentNullException>(() =>
            ReachableExceptionTypePlanner.ResolveDeclaringAssembly(null!));
    }

    [Fact]
    public void PlannerFindsDefinitionAndConstructedExceptionDescriptorsInMemory()
    {
        var assembly = new AssemblyIdentity("Example");
        var exceptionKey = new EntityKey(assembly, 1);
        var childKey = new EntityKey(assembly, 2);
        var exception = CliTypeIdentity.Named(
            assembly,
            "System",
            "Exception",
            isValueType: false);
        var child = CliTypeIdentity.Named(
            assembly,
            "Example",
            "Failure",
            isValueType: false);
        var genericDefinition = CliTypeIdentity.Named(
            assembly,
            "Example",
            "GenericFailure`1",
            isValueType: false);
        var constructed = CliTypeIdentity.GenericInstantiation(
            genericDefinition,
            [child]);
        var planner = new ReachableExceptionTypePlanner(
            new ReachableExceptionTypeValidator());

        var entries = planner.Plan(
            CreateSnapshot(assembly),
            new RecordingTypeIdentityResolver(
                (exceptionKey, exception),
                (childKey, child)),
            new RecordingAssemblyFormatter(),
            new DescriptorSource(
                [
                    new(exceptionKey, 1, 0, 12, 0, 0, null),
                    new(childKey, 2, 1, 12, 0, 0, null),
                ],
                [new(constructed, 3, 2, 12, 0, 0, null)]));

        Assert.Equal([1, 2, 3], entries.Select(entry => entry.TypeId));
        Assert.Equal(constructed.CanonicalName, entries[2].DisplayName);
        Assert.Contains("Version=1.0.0.0", entries[2].AssemblyIdentity,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructedTypeUsesItsGenericDefinitionAssemblyIdentity()
    {
        var assembly = new AssemblyIdentity("Example");
        var definition = CliTypeIdentity.Named(
            assembly,
            "Example",
            "GenericFailure`1",
            isValueType: false);
        var constructed = CliTypeIdentity.GenericInstantiation(definition, [definition]);

        Assert.Equal(assembly, ReachableExceptionTypePlanner.ResolveDeclaringAssembly(constructed));
    }

    [Fact]
    public void PlannerRejectsNullSentinelTypeId()
    {
        var validator = new ReachableExceptionTypeValidator();

        var error = Assert.Throws<InvalidOperationException>(() => validator.Validate(
            [new ReachableExceptionType(0, "Failure, Assembly", "Failure", "Assembly")]));

        Assert.Contains("positive", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlannerRejectsDuplicateTypeIds()
    {
        Assert.Throws<InvalidOperationException>(() => new ReachableExceptionTypeValidator().Validate(
        [
            new(1, "A, A", "A", "A"),
            new(1, "B, A", "B", "A"),
        ]));
    }

    [Fact]
    public void PlannerRejectsDuplicateCanonicalIdentities()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ReachableExceptionTypeValidator().Validate(
            [
                new(1, "Failure, Assembly", "Failure", "Assembly"),
                new(2, "Failure, Assembly", "Failure", "Assembly"),
            ]));

        Assert.Contains("canonical identities", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Failure", "Assembly")]
    [InlineData("Failure, Assembly", "", "Assembly")]
    [InlineData("Failure, Assembly", "Failure", "")]
    public void PlannerRejectsIncompleteIdentities(
        string canonicalIdentity,
        string displayName,
        string assemblyIdentity)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ReachableExceptionTypeValidator().Validate(
            [new(1, canonicalIdentity, displayName, assemblyIdentity)]));

        Assert.Contains("complete", error.Message, StringComparison.Ordinal);
    }

    private static MetadataCompilationSnapshot CreateSnapshot(
        AssemblyIdentity entryAssembly) =>
        new([], entryAssembly, [], [], []);

    private sealed class RecordingTypeIdentityResolver : ITypeIdentityResolver
    {
        private readonly Dictionary<EntityKey, CliTypeIdentity> _identities;

        public RecordingTypeIdentityResolver(
            params (EntityKey Key, CliTypeIdentity Identity)[] identities) =>
            _identities = identities.ToDictionary(
                pair => pair.Key,
                pair => pair.Identity);

        public CliTypeIdentity GetTypeIdentity(EntityKey type) => _identities[type];
    }

    private sealed class RecordingAssemblyFormatter : IAssemblyIdentityFormatter
    {
        public string Format(AssemblyIdentity identity) =>
            $"{identity.Name}, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
    }

    private sealed class DescriptorSource(
        ImmutableArray<TypeDescriptorLayout> definitions,
        ImmutableArray<ConstructedTypeDescriptorLayout> constructed)
        : ITypeDescriptorSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors { get; } = definitions;
        public ImmutableArray<ConstructedTypeDescriptorLayout>
            ConstructedTypeDescriptors
        { get; } = constructed;
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors { get; } = [];
    }
}
