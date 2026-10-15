using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StringConstructionPlanResolverTests
{
    [Theory]
    [InlineData("ReadOnlySpan`1", 1, true, true)]
    [InlineData("Span`1", 1, true, false)]
    [InlineData("ReadOnlySpan`1", 0, true, false)]
    [InlineData("ReadOnlySpan`1", 2, true, false)]
    [InlineData("ReadOnlySpan`1", 1, false, false)]
    public void ResolveRequiresExactlyOneCharacterTypeArgumentForReadOnlySpan(
        string name, int argumentCount, bool character, bool supported)
    {
        var element = character ? CliTypeIdentity.Primitive("char", CliValueKind.I4)
            : CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var span = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(new("System.Private.CoreLib"), "System", name, true),
            [.. Enumerable.Repeat(element, argumentCount)]);
        var signature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), span);
        var resolver = CreateResolver();

        if (supported)
        {
            var plan = resolver.Resolve(signature, 17);
            Assert.Equal(StringConstructionSourceKind.CharacterSpan, plan.SourceKind);
            Assert.Null(plan.StartArgumentIndex);
            Assert.Null(plan.LengthArgumentIndex);
        }
        else
        {
            var error = Assert.Throws<CompilerException>(() => resolver.Resolve(signature, 17));
            Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
            Assert.Equal(17, error.Diagnostic.IlOffset);
        }
    }

    [Fact]
    public void ResolveMapsEverySupportedSignatureToAConstructionPlan()
    {
        var resolver = CreateResolver();
        var characterArray = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("char", CliValueKind.I4));

        var repeated = resolver.Resolve(MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.I4,
            CliValueKind.I4), 1);
        var wholeArray = resolver.Resolve(MethodSignatureModel.Create(
            CliTypeIdentity.Primitive("void", CliValueKind.Void),
            characterArray), 2);
        var arraySlice = resolver.Resolve(MethodSignatureModel.Create(
            CliTypeIdentity.Primitive("void", CliValueKind.Void),
            characterArray,
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4)), 3);

        Assert.Equal(StringConstructionSourceKind.RepeatedCharacter, repeated.SourceKind);
        Assert.Null(repeated.StartArgumentIndex);
        Assert.Equal(1, repeated.LengthArgumentIndex);
        Assert.Equal(StringConstructionSourceKind.CharacterArray, wholeArray.SourceKind);
        Assert.Null(wholeArray.StartArgumentIndex);
        Assert.Null(wholeArray.LengthArgumentIndex);
        Assert.Equal(StringConstructionSourceKind.CharacterArray, arraySlice.SourceKind);
        Assert.Equal(1, arraySlice.StartArgumentIndex);
        Assert.Equal(2, arraySlice.LengthArgumentIndex);
    }

    [Fact]
    public void ResolveRejectsUnsupportedSignaturesDeterministically()
    {
        var resolver = CreateResolver();
        var nonCharacterArray = MethodSignatureModel.Create(
            CliTypeIdentity.Primitive("void", CliValueKind.Void),
            CliTypeIdentity.SzArray(
                CliTypeIdentity.Primitive("i4", CliValueKind.I4)));

        var emptyException = Assert.Throws<CompilerException>(() =>
            resolver.Resolve(MethodSignatureModel.Create(CliValueKind.Void), 11));
        var arrayException = Assert.Throws<CompilerException>(() =>
            resolver.Resolve(nonCharacterArray, 12));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, emptyException.Diagnostic.Code);
        Assert.Equal(11, emptyException.Diagnostic.IlOffset);
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, arrayException.Diagnostic.Code);
        Assert.Equal(12, arrayException.Diagnostic.IlOffset);
    }

    [Fact]
    public void ResolveRequiresASignature()
    {
        var resolver = CreateResolver();

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!, 0));
    }

    [Fact]
    public void GeneratedSignatureKeyPropertiesRemainReadable()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        var resolverType = typeof(StringConstructionPlanResolver);
        var keyType = resolverType.GetNestedType(
            "SignatureKey",
            System.Reflection.BindingFlags.NonPublic)!;
        var parameterKind = resolverType.GetNestedType(
            "ParameterKind",
            System.Reflection.BindingFlags.NonPublic)!;
        var first = Enum.Parse(parameterKind, "Integer");
        var second = Enum.Parse(parameterKind, "CharacterArray");
        var third = Enum.Parse(parameterKind, "Other");
        var constructor = Assert.Single(keyType.GetConstructors(flags));
        var key = constructor.Invoke([3, first, second, third]);

        Assert.Equal(3, keyType.GetProperty("Count", flags)!.GetValue(key));
        Assert.Equal(first, keyType.GetProperty("First", flags)!.GetValue(key));
        Assert.Equal(second, keyType.GetProperty("Second", flags)!.GetValue(key));
        Assert.Equal(third, keyType.GetProperty("Third", flags)!.GetValue(key));
    }

    private static IStringConstructionPlanResolver CreateResolver() =>
        Assert.IsAssignableFrom<IStringConstructionPlanResolver>(
            new StringConstructionPlanResolver());
}
