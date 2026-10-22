using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CustomAttributeDescriptorReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsDeclaringTypesWithoutDecodingConstructorSignaturesOrPayloads(bool memberReference)
    {
        using var fixture = new Fixture(memberReference, includeAttribute: true);
        var descriptor = Assert.Single(fixture.Read());

        Assert.Equal(fixture.Source.Identity, descriptor.Source);
        Assert.Equal(MetadataTokens.GetToken(fixture.Attribute), descriptor.AttributeToken);
        Assert.Equal(MetadataTokens.GetToken(fixture.Constructor), descriptor.ConstructorToken);
        Assert.Same(fixture.AttributeType, descriptor.AttributeType);
        Assert.Equal(fixture.Target.TypeArguments, descriptor.GenericContext.TypeArguments);
        Assert.Empty(descriptor.GenericContext.MethodArguments);
        Assert.Equal(fixture.DeclaringType, Assert.Single(fixture.SignatureRequests));
    }

    [Fact]
    public void UnattributedDeclarationDoesNotResolveAnAttributeType()
    {
        using var fixture = new Fixture(memberReference: false, includeAttribute: false);
        Assert.Empty(fixture.Read());
        Assert.Empty(fixture.SignatureRequests);
    }

    private sealed class Fixture : IDisposable, ITypeDefinitionResolver, IMetadataSignatureTypeResolver
    {
        private readonly MetadataReaderProvider _owner;
        private readonly TypeDefinitionModel _definition;
        internal MetadataAssemblySnapshot Source { get; }
        internal EntityHandle Constructor { get; }
        internal EntityHandle DeclaringType { get; }
        internal CustomAttributeHandle Attribute { get; }
        internal CliTypeIdentity Target { get; }
        internal CliTypeIdentity AttributeType { get; } = CliTypeIdentity.Named(new("Attributes"), "Example", "Marker", false);
        internal List<EntityHandle> SignatureRequests { get; } = [];

        internal Fixture(bool memberReference, bool includeAttribute)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("Descriptors"), default, default, default);
            var type = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Example"),
                metadata.GetOrAddString("Target`1"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var localAttribute = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Example"),
                metadata.GetOrAddString("Marker"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            // Deliberately absent signatures and blobs prove the reader only
            // follows declaration handles. Materialization owns their decoding.
            if (memberReference)
            {
                DeclaringType = metadata.AddTypeSpecification(default);
                Constructor = metadata.AddMemberReference(DeclaringType, metadata.GetOrAddString(".ctor"), default);
            }
            else
            {
                DeclaringType = localAttribute;
                Constructor = metadata.AddMethodDefinition(MethodAttributes.Public, MethodImplAttributes.IL,
                    metadata.GetOrAddString(".ctor"), default, 0, MetadataTokens.ParameterHandle(1));
            }
            if (includeAttribute) Attribute = metadata.AddCustomAttribute(type, Constructor, default);
            var image = new BlobBuilder();
            new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
            _owner = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
            var assembly = new AssemblyIdentity("Descriptors");
            _definition = new(new(assembly, MetadataTokens.GetToken(type)), "Example", "Target`1", false, [], [])
            { GenericArity = 1 };
            Target = CliTypeIdentity.GenericInstantiation(CliTypeIdentity.FromDefinition(_definition),
                [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
            Source = new(assembly, _owner.GetMetadataReader(),
                new Dictionary<int, TypeDefinitionModel>(), new Dictionary<int, FieldDefinitionModel>(),
                new Dictionary<int, MethodDefinitionModel>(), new Dictionary<int, EntityHandle>());
        }

        internal ImmutableArray<CustomAttributeDescriptor> Read() =>
            ((ICustomAttributeDescriptorReader)new CustomAttributeDescriptorReader(
                ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot>.Empty.Add(Source.Identity, Source),
                this, this)).Read(Target);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            Assert.Same(Target, identity);
            return _definition;
        }

        public CliTypeIdentity Resolve(MetadataAssemblySnapshot source, EntityHandle handle,
            CliGenericContext? genericContext = null)
        {
            Assert.Same(Source, source);
            Assert.Equal(Target.TypeArguments, genericContext!.Value.TypeArguments);
            SignatureRequests.Add(handle);
            return AttributeType;
        }

        public void Dispose() => _owner.Dispose();
    }
}
