using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class DispatchCandidateIndexTests
{
    private static readonly AssemblyIdentity Assembly = new("Tests");

    [Fact]
    public void IndexMatchesDeclarationPublishedBeforeReceiver()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DispatchCandidateIndexFactory(null!));
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var contract = Type("IContract");
        var receiver = Type("Implementation");
        var declaration = Declaration("first", 1, contract);

        Assert.Empty(index.Index(new DispatchDeclarationCandidate(
            "first",
            declaration)));

        var candidate = Assert.Single(index.Index(
            new DispatchReceiverCandidate(receiver)));
        Assert.Equal("first", candidate.DispatchKey);
        Assert.Same(declaration, candidate.Declaration);
        Assert.Same(receiver, candidate.Receiver);
        Assert.Equal(1, relationships.CallCount);
    }

    [Fact]
    public void IndexMatchesReceiverPublishedBeforeDeclaration()
    {
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var contract = Type("IContract");
        var receiver = Type("Implementation");
        var declaration = Declaration("first", 1, contract);

        Assert.Empty(index.Index(new DispatchReceiverCandidate(receiver)));

        var candidate = Assert.Single(index.Index(
            new DispatchDeclarationCandidate("first", declaration)));
        Assert.Equal("first", candidate.DispatchKey);
        Assert.Same(declaration, candidate.Declaration);
        Assert.Same(receiver, candidate.Receiver);
        Assert.Equal(1, relationships.CallCount);
    }

    [Fact]
    public void IndexRejectsIncompatibleReceiverContractPairs()
    {
        var relationships = new TestTypeRelationshipClassifier(
            isAssignable: false);
        var index = CreateIndex(relationships).Index;
        var contract = Type("IContract");
        var receiver = Type("Implementation");

        Assert.Empty(index.Index(new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, contract))));
        Assert.Empty(index.Index(new DispatchReceiverCandidate(receiver)));
        Assert.Equal(1, relationships.CallCount);
    }

    [Fact]
    public void IndexClassifiesEachReceiverContractOnceAndPreservesDeclarationOrder()
    {
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var firstContract = Type("IFirst");
        var secondContract = Type("ISecond");

        index.Index(new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, firstContract)));
        index.Index(new DispatchDeclarationCandidate(
            "second",
            Declaration("second", 2, secondContract)));
        index.Index(new DispatchDeclarationCandidate(
            "third",
            Declaration("third", 3, firstContract)));

        var candidates = index.Index(
            new DispatchReceiverCandidate(Type("Implementation")));

        Assert.Equal(["first", "second", "third"],
            candidates.Select(candidate => candidate.DispatchKey));
        Assert.Equal(2, relationships.CallCount);
    }

    [Fact]
    public void IndexPreservesReceiverOrderForLaterDeclarations()
    {
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var first = Type("FirstImplementation");
        var second = Type("SecondImplementation");

        index.Index(new DispatchReceiverCandidate(first));
        index.Index(new DispatchReceiverCandidate(second));

        var candidates = index.Index(new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, Type("IContract"))));

        Assert.Equal([first, second],
            candidates.Select(candidate => candidate.Receiver));
        Assert.Equal(2, relationships.CallCount);
    }

    [Fact]
    public void IndexIgnoresExactDuplicatesAndRejectsConflictingKeys()
    {
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var contract = Type("IContract");
        var receiver = Type("Implementation");
        var declaration = Declaration("first", 1, contract);
        var candidate = new DispatchDeclarationCandidate("first", declaration);

        Assert.Empty(index.Index(candidate));
        Assert.Empty(index.Index(candidate));
        Assert.Single(index.Index(new DispatchReceiverCandidate(receiver)));
        Assert.Empty(index.Index(new DispatchReceiverCandidate(receiver)));

        Assert.Throws<InvalidOperationException>(() => index.Index(
            new DispatchDeclarationCandidate(
                "first",
                Declaration("other", 2, contract))));

        Assert.Single(index.Index(
            new DispatchReceiverCandidate(Type("LaterImplementation"))));
        Assert.Equal(2, relationships.CallCount);
    }

    [Fact]
    public void IndexValidatesInputsBeforePublishingState()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DispatchCandidateIndex(null!));

        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var declaration = Declaration("first", 1, Type("IContract"));

        Assert.Throws<ArgumentException>(() => index.Index(
            new DispatchDeclarationCandidate("", declaration)));
        Assert.Throws<ArgumentNullException>(() => index.Index(
            new DispatchDeclarationCandidate("first", null!)));
        Assert.Throws<ArgumentNullException>(() => index.Index(
            new DispatchReceiverCandidate(null!)));

        Assert.Empty(index.Index(new DispatchDeclarationCandidate(
            "first",
            declaration)));
        Assert.Equal(0, relationships.CallCount);
    }

    [Fact]
    public void IndexDoesNotPublishReceiverWhenClassificationFails()
    {
        var relationships = new TestTypeRelationshipClassifier(
            failuresRemaining: 1);
        var index = CreateIndex(relationships).Index;
        var receiver = Type("Implementation");
        index.Index(new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, Type("IContract"))));

        Assert.Throws<InvalidOperationException>(() => index.Index(
            new DispatchReceiverCandidate(receiver)));

        Assert.Single(index.Index(new DispatchReceiverCandidate(receiver)));
        Assert.Equal(2, relationships.CallCount);
    }

    [Fact]
    public void IndexDoesNotPublishDeclarationWhenClassificationFails()
    {
        var relationships = new TestTypeRelationshipClassifier(
            failuresRemaining: 1);
        var index = CreateIndex(relationships).Index;
        var candidate = new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, Type("IContract")));
        index.Index(new DispatchReceiverCandidate(Type("Implementation")));

        Assert.Throws<InvalidOperationException>(() => index.Index(candidate));

        Assert.Single(index.Index(candidate));
        Assert.Equal(2, relationships.CallCount);
    }

    [Fact]
    public void FactoryCreatesIndependentIndexesForSeparateAnalyses()
    {
        var relationships = new TestTypeRelationshipClassifier();
        var factory = CreateFactory(relationships).Factory;
        var candidate = new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, Type("IContract")));

        Assert.Empty(factory.Create().Index(candidate));
        Assert.Empty(factory.Create().Index(candidate));
    }

    private static DispatchDeclaration Declaration(
        string name,
        int token,
        CliTypeIdentity declaringType) => new(
            "Caller::Run",
            token,
            MethodInstance(name, token, declaringType),
            CilOperation.LoadVirtualFunction);

    private static MethodInstanceModel MethodInstance(
        string name,
        int token,
        CliTypeIdentity declaringType)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        return new(
            new MethodDefinitionModel(
                new EntityKey(Assembly, token),
                new EntityKey(Assembly, token + 100),
                name,
                false,
                signature,
                1)
            {
                IsVirtual = true,
            },
            declaringType,
            [],
            signature);
    }

    private static DispatchCandidateIndexFixture CreateIndex(
        TestTypeRelationshipClassifier relationships) =>
        new(new DispatchCandidateIndex(relationships));

    private static DispatchCandidateIndexFactoryFixture CreateFactory(
        TestTypeRelationshipClassifier relationships) =>
        new(new DispatchCandidateIndexFactory(relationships));

    private static CliTypeIdentity Type(string name) =>
        CliTypeIdentity.Named(
            Assembly,
            "Tests",
            name,
            isValueType: false);

    private sealed class DispatchCandidateIndexFixture(
        IDispatchCandidateIndex index)
    {
        public IDispatchCandidateIndex Index { get; } = index;
    }

    private sealed class DispatchCandidateIndexFactoryFixture(
        IDispatchCandidateIndexFactory factory)
    {
        public IDispatchCandidateIndexFactory Factory { get; } = factory;
    }
}
