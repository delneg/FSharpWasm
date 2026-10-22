using NetWasm.Compiler.Core;
using System.Collections.Immutable;

using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests;

public sealed class ImplicitExceptionDiscoveryTests
{
    [Theory]
    [InlineData(CilOperation.AddChecked)]
    [InlineData(CilOperation.AddCheckedUnsigned)]
    [InlineData(CilOperation.SubtractChecked)]
    [InlineData(CilOperation.SubtractCheckedUnsigned)]
    [InlineData(CilOperation.MultiplyChecked)]
    [InlineData(CilOperation.MultiplyCheckedUnsigned)]
    public void CheckedBinaryOperationsRequireManagedOverflowException(
        CilOperation operation)
    {
        var discovered = new ImplicitExceptionDiscovery(
                new UnusedMethodRepository(),
                new UnusedSymbolFormatter(),
                new StringConstructionExceptionRequirementProvider(),
                RuntimeExceptions())
            .Discover(new CilInstruction(0, 1, operation, new CilOperand.None()));

        var requirement = Assert.Single(discovered);

        Assert.Equal(ManagedExceptionKind.Overflow, requirement.Kind);
        Assert.Equal("System.OverflowException", requirement.TypeName);
    }

    [Fact]
    public void ValueTypeElementAddressesNeedNoCovarianceCheck()
    {
        var discovered = new ImplicitExceptionDiscovery(
                new UnusedMethodRepository(),
                new UnusedSymbolFormatter(),
                new StringConstructionExceptionRequirementProvider(),
                RuntimeExceptions())
            .Discover(new CilInstruction(0, 1, CilOperation.LoadArrayElementAddress,
                new CilOperand.TypeIdentity(CliTypeIdentity.Primitive("i4", CliValueKind.I4))));

        Assert.DoesNotContain(discovered, requirement => requirement.Kind == ManagedExceptionKind.ArrayTypeMismatch);
    }

    [Theory]
    [InlineData(CilOperation.NewObject, ManagedExceptionKind.OutOfMemory)]
    [InlineData(CilOperation.Box, ManagedExceptionKind.OutOfMemory)]
    [InlineData(CilOperation.Unbox, ManagedExceptionKind.NullReference, ManagedExceptionKind.InvalidCast)]
    [InlineData(CilOperation.UnboxAny, ManagedExceptionKind.NullReference, ManagedExceptionKind.InvalidCast)]
    [InlineData(CilOperation.ConvertNumeric, ManagedExceptionKind.Overflow)]
    [InlineData(CilOperation.NewArray, ManagedExceptionKind.OutOfMemory,
        ManagedExceptionKind.Overflow)]
    [InlineData(CilOperation.NewRectangularArray, ManagedExceptionKind.OutOfMemory,
        ManagedExceptionKind.Overflow)]
    [InlineData(CilOperation.NewBoundedRectangularArray, ManagedExceptionKind.OutOfMemory,
        ManagedExceptionKind.Overflow, ManagedExceptionKind.ArgumentOutOfRange)]
    [InlineData(CilOperation.LoadField, ManagedExceptionKind.NullReference)]
    [InlineData(CilOperation.StoreField, ManagedExceptionKind.NullReference)]
    [InlineData(CilOperation.LoadArrayLength, ManagedExceptionKind.NullReference)]
    [InlineData(CilOperation.CompareExchange, ManagedExceptionKind.NullReference)]
    [InlineData(CilOperation.LoadArrayElementReference, ManagedExceptionKind.NullReference,
        ManagedExceptionKind.IndexOutOfRange)]
    [InlineData(CilOperation.LoadArrayElement, ManagedExceptionKind.NullReference,
        ManagedExceptionKind.IndexOutOfRange)]
    [InlineData(CilOperation.LoadArrayElementAddress, ManagedExceptionKind.NullReference,
        ManagedExceptionKind.IndexOutOfRange, ManagedExceptionKind.ArrayTypeMismatch)]
    [InlineData(CilOperation.StoreArrayElementReference, ManagedExceptionKind.NullReference,
        ManagedExceptionKind.IndexOutOfRange, ManagedExceptionKind.ArrayTypeMismatch)]
    [InlineData(CilOperation.StoreArrayElement, ManagedExceptionKind.NullReference,
        ManagedExceptionKind.IndexOutOfRange, ManagedExceptionKind.ArrayTypeMismatch)]
    [InlineData(CilOperation.LoadRectangularArrayElementAddress, ManagedExceptionKind.NullReference,
        ManagedExceptionKind.IndexOutOfRange, ManagedExceptionKind.ArrayTypeMismatch)]
    [InlineData(CilOperation.LoadStaticDataElement, ManagedExceptionKind.IndexOutOfRange)]
    [InlineData(CilOperation.Divide, ManagedExceptionKind.DivideByZero,
        ManagedExceptionKind.Overflow)]
    [InlineData(CilOperation.DivideUnsigned, ManagedExceptionKind.DivideByZero,
        ManagedExceptionKind.Overflow)]
    [InlineData(CilOperation.Remainder, ManagedExceptionKind.DivideByZero)]
    [InlineData(CilOperation.RemainderUnsigned, ManagedExceptionKind.DivideByZero)]
    [InlineData(CilOperation.CheckFinite, ManagedExceptionKind.Arithmetic)]
    [InlineData(CilOperation.CastClass, ManagedExceptionKind.InvalidCast)]
    [InlineData(CilOperation.CallIndirect, ManagedExceptionKind.InvalidCast)]
    [InlineData(CilOperation.MaterializeType, ManagedExceptionKind.OutOfMemory)]
    [InlineData(CilOperation.MaterializeMethod, ManagedExceptionKind.Argument)]
    [InlineData(CilOperation.MaterializeField, ManagedExceptionKind.Argument)]
    [InlineData(CilOperation.GetObjectType, ManagedExceptionKind.NullReference,
        ManagedExceptionKind.OutOfMemory)]
    [InlineData(CilOperation.Throw, ManagedExceptionKind.NullReference)]
    public void DiscoveryReportsTheOperationSpecificManagedExceptions(
        CilOperation operation,
        params ManagedExceptionKind[] expected)
    {
        var instruction = new CilInstruction(
            0,
            1,
            operation,
            operation == CilOperation.ConvertNumeric
                ? new CilOperand.NumericConversion(32, false, true, false, false)
                : new CilOperand.None());
        var discovered = Discover(instruction);

        Assert.Equal(expected, discovered.Select(requirement => requirement.Kind));
    }

    [Fact]
    public void DiscoveryRecognizesSpecialStringCalls()
    {
        var discovery = new ImplicitExceptionDiscovery(
            new ConfigurableMethodRepository(),
            new StringSymbolFormatter(),
            new StringConstructionExceptionRequirementProvider(),
            RuntimeExceptions(
                (new EntityKey(Assembly, 3), RuntimeIntrinsic.StringCharacterAt),
                (new EntityKey(Assembly, 4), RuntimeIntrinsic.StringLength)));

        Assert.Empty(discovery.Discover(new CilInstruction(
            0,
            1,
            CilOperation.Nop,
            new CilOperand.None())));

        var constructor = new MethodDefinitionModel(
            new EntityKey(Assembly, 2),
            new EntityKey(Assembly, 1),
            ".ctor",
            false,
            MethodSignatureModel.Create(
                CliValueKind.Void,
                CliValueKind.I4,
                CliValueKind.I4),
            0);
        var newObject = discovery.Discover(new CilInstruction(
            0,
            1,
            CilOperation.NewObject,
            new CilOperand.Entity(constructor.Key)));
        Assert.Equal(
            [ManagedExceptionKind.OutOfMemory, ManagedExceptionKind.ArgumentOutOfRange],
            newObject.Select(requirement => requirement.Kind));

        var characterArray = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("char", CliValueKind.I4));
        var arrayConstructor = constructor with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("void", CliValueKind.Void),
                characterArray),
        };
        var arrayObject = discovery.Discover(new CilInstruction(
            0,
            1,
            CilOperation.NewObject,
            new CilOperand.MethodInstance(new MethodInstanceModel(
                arrayConstructor,
                CliTypeIdentity.Named(
                    Assembly,
                    "System",
                    "String",
                    isValueType: false),
                [],
                arrayConstructor.Signature))));
        Assert.Equal(
            [ManagedExceptionKind.OutOfMemory, ManagedExceptionKind.ArgumentNull],
            arrayObject.Select(requirement => requirement.Kind));

        var call = new MethodDefinitionModel(
            new EntityKey(Assembly, 3),
            new EntityKey(Assembly, 4),
            "get_Chars",
            true,
                MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
            0);
        var virtualCall = discovery.Discover(new CilInstruction(
            0,
            1,
            CilOperation.CallVirtual,
            new CilOperand.MethodInstance(new MethodInstanceModel(
                call,
                CliTypeIdentity.Named(
                    Assembly,
                    "System",
                    "String",
                    isValueType: false),
                [],
                call.Signature))));
        Assert.Equal(
            [
                ManagedExceptionKind.NullReference,
                ManagedExceptionKind.InvalidCast,
                ManagedExceptionKind.IndexOutOfRange,
            ],
            virtualCall.Select(requirement => requirement.Kind));

        var length = call with
        {
            Key = new EntityKey(Assembly, 4),
            Name = "get_Length",
        };
        var lengthCall = discovery.Discover(new CilInstruction(
            0,
            1,
            CilOperation.Call,
            new CilOperand.Entity(length.Key)));
        Assert.Equal(
            [ManagedExceptionKind.NullReference],
            lengthCall.Select(requirement => requirement.Kind));
    }

    [Theory]
    [InlineData("System.Array", "GetLength", true)]
    [InlineData("System.Array", "Other", false)]
    [InlineData("Other", "GetLength", false)]
    public void VirtualArrayLengthRequiresBoundsValidation(string declaringName, string name, bool bounds)
    {
        var method = new MethodDefinitionModel(new(Assembly, 41), new(Assembly, 42), name, false,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4), 0);
        var discovery = new ImplicitExceptionDiscovery(new SingleMethodRepository(method),
            new FixedTypeSymbolFormatter(declaringName),
            new StringConstructionExceptionRequirementProvider(), RuntimeExceptions());

        var exceptions = discovery.Discover(new(0, 1, CilOperation.CallVirtual, new CilOperand.Entity(method.Key)));

        Assert.Equal(bounds ? 3 : 2, exceptions.Length);
        Assert.Equal(bounds, exceptions.Any(item => item.Kind == ManagedExceptionKind.IndexOutOfRange));
        Assert.Equal([ManagedExceptionKind.OutOfMemory],
            discovery.Discover(new(0, 1, CilOperation.NewObject, new CilOperand.Entity(method.Key)))
                .Select(item => item.Kind));
    }

    [Fact]
    public void StringConstructionExceptionPolicyCoversEverySignatureShape()
    {
        var provider = Assert.IsAssignableFrom<
            IStringConstructionExceptionRequirementProvider>(
                new StringConstructionExceptionRequirementProvider());
        var characterArray = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("char", CliValueKind.I4));

        Assert.Equal(
            [ManagedExceptionKind.OutOfMemory],
            Discover(MethodSignatureModel.Create(CliValueKind.Void)));
        Assert.Equal(
            [ManagedExceptionKind.OutOfMemory, ManagedExceptionKind.ArgumentOutOfRange],
            Discover(MethodSignatureModel.Create(
                CliValueKind.Void,
                CliValueKind.I4,
                CliValueKind.I4)));
        Assert.Equal(
            [ManagedExceptionKind.OutOfMemory, ManagedExceptionKind.ArgumentNull],
            Discover(MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("void", CliValueKind.Void),
                characterArray)));
        Assert.Equal(
            [
                ManagedExceptionKind.OutOfMemory,
                ManagedExceptionKind.ArgumentNull,
                ManagedExceptionKind.ArgumentOutOfRange,
            ],
            Discover(MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("void", CliValueKind.Void),
                characterArray,
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                CliTypeIdentity.Primitive("i4", CliValueKind.I4))));
        Assert.Equal(
            [ManagedExceptionKind.OutOfMemory],
            Discover(MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("void", CliValueKind.Void),
                CliTypeIdentity.SzArray(
                    CliTypeIdentity.Primitive("i4", CliValueKind.I4)))));

        ManagedExceptionKind[] Discover(MethodSignatureModel signature) =>
            [.. provider.Discover(new MethodDefinitionModel(
                new EntityKey(Assembly, 30),
                new EntityKey(Assembly, 1),
                ".ctor",
                false,
                signature,
                0)).Select(requirement => requirement.Kind)];
    }

    [Fact]
    public void DiscoveryLeavesUnmanagedAndUncheckedOperationsUnchanged()
    {
        var uncheckedConversion = new CilInstruction(
            0,
            1,
            CilOperation.ConvertNumeric,
            new CilOperand.NumericConversion(32, false, false, false, false));
        var nonEntityCall = new CilInstruction(
            0,
            1,
            CilOperation.Call,
            new CilOperand.None());

        Assert.Empty(Discover(uncheckedConversion));
        Assert.Empty(Discover(nonEntityCall));
        Assert.Empty(Discover(new CilInstruction(0, 1, CilOperation.Add, new CilOperand.None())));
        Assert.Empty(Discover(new CilInstruction(0, 1, CilOperation.ConvertNumeric, new CilOperand.None())));
        Assert.Equal(2, Discover(new CilInstruction(0, 1, CilOperation.CallVirtual, new CilOperand.None())).Length);
    }

    [Theory]
    [InlineData("Alloc")]
    [InlineData("Realloc")]
    [InlineData("AlignedAllocCore")]
    [InlineData("AlignedReallocCore")]
    public void NativeAllocationCallsRequireOutOfMemoryException(string methodName)
    {
        var method = new MethodDefinitionModel(
            new EntityKey(Assembly, 20),
            new EntityKey(Assembly, 21),
            methodName,
            true,
            MethodSignatureModel.Create(CliValueKind.NativeInt, CliValueKind.NativeInt),
            0);
        var discovery = new ImplicitExceptionDiscovery(
            new SingleMethodRepository(method),
            new NativeMemorySymbolFormatter(),
            new StringConstructionExceptionRequirementProvider(),
            RuntimeExceptions((method.Key, methodName switch
            {
                "Alloc" => RuntimeIntrinsic.NativeMemoryAlloc,
                "Realloc" => RuntimeIntrinsic.NativeMemoryRealloc,
                "AlignedAllocCore" => RuntimeIntrinsic.NativeMemoryAlignedAlloc,
                _ => RuntimeIntrinsic.NativeMemoryAlignedRealloc,
            })));

        var requirement = Assert.Single(discovery.Discover(new CilInstruction(
            0,
            1,
            CilOperation.Call,
            new CilOperand.Entity(method.Key))));

        Assert.Equal(ManagedExceptionKind.OutOfMemory, requirement.Kind);
    }

    [Fact]
    public void UnsafeUnboxRequiresNullReferenceAndInvalidCastExceptions()
    {
        var method = new MethodDefinitionModel(
            new EntityKey(Assembly, 22),
            new EntityKey(Assembly, 23),
            "Unbox",
            true,
            MethodSignatureModel.Create(
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedReference),
            1);
        var discovery = new ImplicitExceptionDiscovery(
            new SingleMethodRepository(method),
            new UnsafeSymbolFormatter(),
            new StringConstructionExceptionRequirementProvider(),
            RuntimeExceptions((method.Key, RuntimeIntrinsic.UnsafeUnbox)));

        var requirements = discovery.Discover(new CilInstruction(
            0,
            1,
            CilOperation.Call,
            new CilOperand.MethodInstance(new MethodInstanceModel(
                method,
                CliTypeIdentity.Named(
                    Assembly,
                    "System.Runtime.CompilerServices",
                    "Unsafe",
                    false),
                [CliTypeIdentity.FromStackKind(CliValueKind.I4)],
                method.Signature))));

        Assert.Equal(
            [ManagedExceptionKind.NullReference, ManagedExceptionKind.InvalidCast],
            requirements.Select(requirement => requirement.Kind));
    }

    private static ImmutableArray<ReachabilityExceptionRequirement> Discover(
        CilInstruction instruction) =>
        new ImplicitExceptionDiscovery(
            new ConfigurableMethodRepository(),
            new StringSymbolFormatter(),
            new StringConstructionExceptionRequirementProvider(),
            RuntimeExceptions(
                (new EntityKey(Assembly, 3), RuntimeIntrinsic.StringCharacterAt),
                (new EntityKey(Assembly, 4), RuntimeIntrinsic.StringLength)))
            .Discover(instruction);

    private static RuntimeIntrinsicExceptionRequirementProvider RuntimeExceptions(
        params (EntityKey Method, RuntimeIntrinsic Intrinsic)[] entries) =>
        new RuntimeIntrinsicExceptionRequirementProvider(
            new FixedRuntimeIntrinsicRegistry(entries));

    private static readonly AssemblyIdentity Assembly = new("in-memory");

    private sealed class ConfigurableMethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => key.MetadataToken switch
        {
            2 => new MethodDefinitionModel(
                key,
                new EntityKey(Assembly, 1),
                ".ctor",
                false,
                MethodSignatureModel.Create(
                    CliValueKind.Void,
                    CliValueKind.I4,
                    CliValueKind.I4),
                0),
            3 => new MethodDefinitionModel(
                key,
                new EntityKey(Assembly, 4),
                "get_Chars",
                true,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
                0),
            4 => new MethodDefinitionModel(
                key,
                new EntityKey(Assembly, 4),
                "get_Length",
                true,
                MethodSignatureModel.Create(CliValueKind.I4),
                0),
            _ => new MethodDefinitionModel(
                key,
                new EntityKey(Assembly, 4),
                "Run",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                0),
        };
    }

    private sealed class StringSymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.MetadataToken switch
        {
            1 or 4 => "System.String",
            _ => "Tests.Fixture",
        };

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class UnusedMethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) =>
            throw new InvalidOperationException();
    }

    private sealed class SingleMethodRepository(MethodDefinitionModel method) :
        IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => method;
    }

    private sealed class FixedRuntimeIntrinsicRegistry(
        IEnumerable<(EntityKey Method, RuntimeIntrinsic Intrinsic)> entries) :
        IRuntimeIntrinsicRegistry
    {
        private readonly Dictionary<EntityKey, RuntimeIntrinsic> _entries =
            entries.ToDictionary(entry => entry.Method, entry => entry.Intrinsic);

        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic) =>
            _entries.TryGetValue(method, out intrinsic);
    }

    private sealed class NativeMemorySymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) =>
            "System.Runtime.InteropServices.NativeMemory";

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class UnsafeSymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) =>
            "System.Runtime.CompilerServices.Unsafe";

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class UnusedSymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) => throw new InvalidOperationException();

        public string Format(MethodDefinitionModel method) =>
            throw new InvalidOperationException();
    }

    private sealed class FixedTypeSymbolFormatter(string name) : ISymbolFormatter
    {
        public string Format(EntityKey key) => name;
        public string Format(MethodDefinitionModel method) => method.Name;
    }
}
