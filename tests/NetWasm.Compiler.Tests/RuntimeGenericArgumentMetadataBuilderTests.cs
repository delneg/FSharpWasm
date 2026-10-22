using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class RuntimeGenericArgumentMetadataBuilderTests
{
    private static readonly AssemblyIdentity Assembly = new("fixtures");

    [Fact]
    public void ConstructorRejectsMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeGenericArgumentMetadataBuilder(null!, new()));
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeGenericArgumentMetadataBuilder(Program(), null!));
    }

    [Fact]
    public void NoDemandLeavesFactsUnchanged()
    {
        var state = new ManagedStaticDataBuildState();
        var boxIdentity = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "Box`1",
            false);
        var boxDefinition = Definition(1, "Box`1", "T");
        state.PendingTypeFacts.Add(Facts(
            boxIdentity,
            boxDefinition,
            typeId: 7));

        new RuntimeGenericArgumentMetadataBuilder(Program(), state).Build();

        Assert.Empty(state.PendingTypeFacts[0].GenericArgumentTypeIds);
        Assert.Empty(state.MetadataTypeDescriptors);
    }

    [Fact]
    public void OpenDefinitionsReceiveOwnerScopedParameterIdentities()
    {
        var state = new ManagedStaticDataBuildState();
        var boxIdentity = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "Box`1",
            false);
        var boxDefinition = Definition(1, "Box`1", "T");
        state.PendingTypeFacts.Add(Facts(
            boxIdentity,
            boxDefinition,
            typeId: 7));
        state.PendingTypeFacts.Add(Facts(
            CliTypeIdentity.Named(Assembly, "Fixtures", "Other`1", false),
            Definition(2, "Other`1", "T"),
            typeId: 8));
        state.PendingTypeFacts.Add(Facts(
            CliTypeIdentity.GenericInstantiation(
                boxIdentity,
                [CliTypeIdentity.GenericParameter(method: false, index: 0)]),
            boxDefinition,
            typeId: 9));
        var program = Program(
            requiresGenericArguments: true,
            typeNamePayload: RuntimeTypeNamePayload.Name |
                RuntimeTypeNamePayload.DisplayName);

        new RuntimeGenericArgumentMetadataBuilder(program, state).Build();

        var firstId = Assert.Single(state.PendingTypeFacts[0].GenericArgumentTypeIds);
        var secondId = Assert.Single(state.PendingTypeFacts[1].GenericArgumentTypeIds);
        var constructedId = Assert.Single(
            state.PendingTypeFacts[2].GenericArgumentTypeIds);
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(firstId, constructedId);
        Assert.Equal(2, state.MetadataTypeDescriptors.Count);
        Assert.All(
            state.MetadataTypeDescriptors,
            descriptor => Assert.Equal(
                CliTypeShape.GenericTypeParameter,
                descriptor.Type.Shape));
        var parameters = state.PendingTypeFacts.Skip(3).ToArray();
        Assert.Equal(2, parameters.Length);
        Assert.All(parameters, parameter => Assert.Equal("T", parameter.Names!.Name));
        Assert.NotEqual(
            parameters[0].Identity.CanonicalName,
            parameters[1].Identity.CanonicalName);
    }

    [Fact]
    public void ClosedInstantiationUsesExistingArgumentTypeId()
    {
        var state = new ManagedStaticDataBuildState();
        var argument = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var definitionIdentity = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "Box`1",
            false);
        var definition = Definition(1, "Box`1", "T");
        state.PendingTypeFacts.Add(Facts(argument, Definition(2, "Int32"), typeId: 2));
        state.PendingTypeFacts.Add(Facts(
            CliTypeIdentity.Named(Assembly, "Fixtures", "Ordinary", false),
            Definition(3, "Ordinary"),
            typeId: 4));
        state.PendingTypeFacts.Add(Facts(
            CliTypeIdentity.GenericInstantiation(definitionIdentity, [argument]),
            definition,
            typeId: 3));

        new RuntimeGenericArgumentMetadataBuilder(
            Program(requiresGenericArguments: true),
            state).Build();

        Assert.Empty(state.PendingTypeFacts[0].GenericArgumentTypeIds);
        Assert.Empty(state.PendingTypeFacts[1].GenericArgumentTypeIds);
        Assert.Equal(2, Assert.Single(state.PendingTypeFacts[2].GenericArgumentTypeIds));
        Assert.Empty(state.MetadataTypeDescriptors);
    }

    [Fact]
    public void MissingClosedArgumentDescriptorIsRejected()
    {
        var state = new ManagedStaticDataBuildState();
        var argument = CliTypeIdentity.Named(Assembly, "Fixtures", "Missing", false);
        var definitionIdentity = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "Box`1",
            false);
        state.PendingTypeFacts.Add(Facts(
            CliTypeIdentity.GenericInstantiation(definitionIdentity, [argument]),
            Definition(1, "Box`1", "T"),
            typeId: 3));

        var exception = Assert.Throws<CompilerException>(() =>
            new RuntimeGenericArgumentMetadataBuilder(
                Program(requiresGenericArguments: true),
                state).Build());

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void OpenDefinitionRequiresOneNamePerGenericParameter()
    {
        var state = new ManagedStaticDataBuildState();
        var identity = CliTypeIdentity.Named(Assembly, "Fixtures", "Pair`2", false);
        state.PendingTypeFacts.Add(Facts(
            identity,
            Definition(1, "Pair`2", "T") with { GenericArity = 2 },
            typeId: 1));

        var exception = Assert.Throws<CompilerException>(() =>
            new RuntimeGenericArgumentMetadataBuilder(
                Program(requiresGenericArguments: true),
                state).Build());

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void ConstructedGenericParameterRequiresDefinitionAndValidIndex()
    {
        var identity = CliTypeIdentity.Named(Assembly, "Fixtures", "Box`1", false);
        var openArgument = CliTypeIdentity.GenericParameter(method: false, index: 0);
        var missingDefinition = new ManagedStaticDataBuildState();
        missingDefinition.PendingTypeFacts.Add(Facts(
            CliTypeIdentity.GenericInstantiation(identity, [openArgument]),
            definition: null,
            typeId: 1));

        var missing = Assert.Throws<CompilerException>(() =>
            new RuntimeGenericArgumentMetadataBuilder(
                Program(requiresGenericArguments: true),
                missingDefinition).Build());
        Assert.Equal(DiagnosticCode.RuntimeContract, missing.Diagnostic.Code);

        var invalidIndex = new ManagedStaticDataBuildState();
        invalidIndex.PendingTypeFacts.Add(Facts(
            CliTypeIdentity.GenericInstantiation(
                identity,
                [CliTypeIdentity.GenericParameter(method: false, index: 1)]),
            Definition(1, "Box`1", "T"),
            typeId: 1));

        var invalid = Assert.Throws<CompilerException>(() =>
            new RuntimeGenericArgumentMetadataBuilder(
                Program(requiresGenericArguments: true),
                invalidIndex).Build());
        Assert.Equal(DiagnosticCode.RuntimeContract, invalid.Diagnostic.Code);
    }

    [Theory]
    [InlineData(RuntimeTypeNamePayload.None, null, null)]
    [InlineData(RuntimeTypeNamePayload.Name, "T", null)]
    [InlineData(RuntimeTypeNamePayload.DisplayName, null, "T")]
    public void OpenParameterNamesHonorTheRequestedPayload(
        RuntimeTypeNamePayload payload,
        string? expectedName,
        string? expectedDisplayName)
    {
        var state = new ManagedStaticDataBuildState();
        var identity = CliTypeIdentity.Named(Assembly, "Fixtures", "Box`1", false);
        state.PendingTypeFacts.Add(Facts(
            identity,
            Definition(1, "Box`1", "T"),
            typeId: 1));

        new RuntimeGenericArgumentMetadataBuilder(
            Program(requiresGenericArguments: true, typeNamePayload: payload),
            state).Build();

        var names = state.PendingTypeFacts[1].Names;
        if (payload == RuntimeTypeNamePayload.None)
        {
            Assert.Null(names);
        }
        else
        {
            Assert.Equal(expectedName, names!.Name);
            Assert.Equal(expectedDisplayName, names.DisplayName);
        }
    }

    [Fact]
    public void MalformedNullGenericParameterNameDoesNotCreateNameMetadata()
    {
        var state = new ManagedStaticDataBuildState();
        var identity = CliTypeIdentity.Named(Assembly, "Fixtures", "Box`1", false);
        state.PendingTypeFacts.Add(Facts(
            identity,
            Definition(1, "Box`1", [null!]),
            typeId: 1));

        new RuntimeGenericArgumentMetadataBuilder(
            Program(
                requiresGenericArguments: true,
                typeNamePayload: RuntimeTypeNamePayload.Name),
            state).Build();

        Assert.Null(state.PendingTypeFacts[1].Names);
    }

    [Fact]
    public void NamedIdentityWithoutADefinitionHasNoGenericArguments()
    {
        var state = new ManagedStaticDataBuildState();
        state.PendingTypeFacts.Add(Facts(
            CliTypeIdentity.Named(Assembly, "Fixtures", "Missing", false),
            definition: null,
            typeId: 1));

        new RuntimeGenericArgumentMetadataBuilder(
            Program(requiresGenericArguments: true),
            state).Build();

        Assert.Empty(state.PendingTypeFacts[0].GenericArgumentTypeIds);
    }

    private static PendingRuntimeTypeFacts Facts(
        CliTypeIdentity identity,
        TypeDefinitionModel? definition,
        int typeId) => new(
        identity,
        definition,
        typeId,
        BaseTypeId: 0,
        AssignableTypeIdsAddress: 0,
        AssignableTypeIdCount: 0,
        DelegateInvokeDescriptor: null,
        Names: null);

    private static ReachableProgram Program(
        bool requiresGenericArguments = false,
        RuntimeTypeNamePayload typeNamePayload = RuntimeTypeNamePayload.None) => new(
        EntryPoint: null,
        Methods: [],
        Types: [],
        Fields: [],
        StaticInitializers: [],
        StringLiterals: [],
        Exports: [],
        AllocatingMethods: [],
        RootMaps: [],
        Finalizers: [],
        ImplicitExceptions: [])
        {
            RequiresGenericArguments = requiresGenericArguments,
            TypeNamePayload = typeNamePayload,
        };

    private static TypeDefinitionModel Definition(
        int token,
        string name,
        params string[] genericParameters) => new(
        new EntityKey(Assembly, token),
        "Fixtures",
        name,
        IsValueType: false,
        [],
        [])
        {
            GenericArity = genericParameters.Length,
            GenericParameterNames = [.. genericParameters],
        };
}
