using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StringConstructionEmitterTests
{
    [Theory]
    [MemberData(nameof(SpanContracts))]
    public void CharacterSpanRequiresACompatibleFieldAndValueLayout(WasmTarget target, string contract)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var span = new SpanLayout(layouts.Target, contract);
        var emitter = new StringConstructionEmitter(span, span, layouts,
            new AddressInstructionEmitter(layouts), span, span, layouts, layouts,
            WasmRuntimeImports.CreateCatalog(), new ImplicitExceptionEmitter(layouts, layouts, 7),
            new StringConstructionPlanResolver());
        var signature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), span.Type);
        var request = CreateInstructionRequest(CilOperation.NewObject, [CliValueKind.ValueType]);
        var writer = new RecordingInstructionWriter();

        if (contract != "valid")
        {
            var error = Assert.Throws<CompilerException>(() => emitter.Emit(request, writer, signature, 0));
            Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
            Assert.Empty(writer.ToInstructions());
            Assert.Equal(CliValueKind.ValueType, Assert.Single(request.Stack));
            return;
        }

        emitter.Emit(request, writer, signature, 0);

        Assert.Equal(CliValueKind.ManagedReference, Assert.Single(request.Stack));
        var instructions = writer.ToInstructions();
        Assert.Contains(instructions, item => item.Opcode == WasmOpcodes.I32Load &&
            item.Operand.Offset == (uint)layouts.Target.AddressSize);
        Assert.Contains(instructions, item => item.Opcode == (target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Load : WasmOpcodes.I32Load) && item.Operand.Offset == 0);
        Assert.Contains(instructions, item => item.Opcode == WasmOpcodes.Call);
        Assert.Contains(instructions, item => item.Opcode == (target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        Assert.Contains(WasmOpcodes.MemoryCopy, writer.ToArray());
    }

    public static TheoryData<WasmTarget, string> SpanContracts()
    {
        var data = new TheoryData<WasmTarget, string>();
        string[] contracts = ["valid", "reference-missing", "length-missing", "reference-kind",
            "element", "length-kind", "reference-size", "length-size", "reference-negative",
            "reference-outside", "length-negative", "length-outside", "byref-map"];
        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            foreach (var contract in contracts) data.Add(target, contract);
        return data;
    }

    private sealed class SpanLayout(WasmTargetLayout target, string contract) : ITypeDefinitionResolver,
        IFieldRepository, IValueLayoutProvider, IInstanceFieldLayoutProvider
    {
        private static readonly AssemblyIdentity Assembly = new("span-construction-tests");
        private static readonly EntityKey Owner = new(Assembly, 0x02000001);
        private static readonly EntityKey Reference = new(Assembly, 0x04000001);
        private static readonly EntityKey Length = new(Assembly, 0x04000002);
        private static readonly CliTypeIdentity Character = CliTypeIdentity.Primitive("char", CliValueKind.I4);
        public CliTypeIdentity Type { get; } = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "ReadOnlySpan`1", true), [Character]);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(Owner, "System", "ReadOnlySpan`1", true, [Reference, Length], []);

        public FieldDefinitionModel GetField(EntityKey key)
        {
            var reference = key == Reference;
            var name = reference ? "_reference" : "_length";
            if (contract == (reference ? "reference-missing" : "length-missing")) name = "other";
            var type = reference ? CliTypeIdentity.ManagedByReference(Character)
                : CliTypeIdentity.FromStackKind(CliValueKind.I4);
            if (reference && contract == "reference-kind") type = Character;
            if (reference && contract == "element")
                type = CliTypeIdentity.ManagedByReference(CliTypeIdentity.FromStackKind(CliValueKind.I4));
            if (!reference && contract == "length-kind") type = CliTypeIdentity.FromStackKind(CliValueKind.I8);
            return new(key, Owner, name, type, false);
        }

        public ValueLayout GetValueLayout(CliTypeIdentity type) => new(type, target.AddressSize + 4, target.AddressSize, [])
        { ByReferenceOffsets = contract == "byref-map" ? [] : [0] };

        public FieldLayout GetFieldLayout(FieldInstanceModel field) => GetFieldLayout(field.Definition.Key);

        public FieldLayout GetFieldLayout(EntityKey field)
        {
            var reference = field == Reference;
            var offset = reference ? 0 : target.AddressSize;
            var size = reference ? target.AddressSize : 4;
            if (contract == (reference ? "reference-negative" : "length-negative")) offset = -1;
            if (contract == (reference ? "reference-outside" : "length-outside")) offset = 64;
            if (contract == (reference ? "reference-size" : "length-size")) size = 1;
            return new(offset) { Size = size };
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RepeatedCharacterConstructorAllocatesForEachTarget(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.I4,
            CliValueKind.I4);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.I4, CliValueKind.I4]);

        emitter.Emit(request, GetCodeWriter(request), signature, 0);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        var code = GetCodeBytes(request);
        Assert.Contains(WasmOpcodes.Call, code);
        Assert.Contains(target == WasmTarget.Wasm64
            ? WasmOpcodes.I64EqualZero
            : WasmOpcodes.I32EqualZero, code);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CharacterArrayConstructorCopiesEveryCharacterForEachTarget(
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(
            CliTypeIdentity.Primitive("void", CliValueKind.Void),
            CliTypeIdentity.SzArray(
                CliTypeIdentity.Primitive("char", CliValueKind.I4)));
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.ManagedReference]);

        emitter.Emit(request, GetCodeWriter(request), signature, 0);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        var code = GetCodeBytes(request);
        Assert.Contains(WasmOpcodes.MemoryCopy, code);
        Assert.Contains(target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Multiply
            : WasmOpcodes.I32Multiply, code);
        Assert.Equal(
            target == WasmTarget.Wasm64,
            code.Contains(WasmOpcodes.I64ExtendI32Unsigned));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CharacterArraySliceConstructorValidatesAndCopiesTheSelectedRange(
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(
            CliTypeIdentity.Primitive("void", CliValueKind.Void),
            CliTypeIdentity.SzArray(
                CliTypeIdentity.Primitive("char", CliValueKind.I4)),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4]);

        emitter.Emit(request, GetCodeWriter(request), signature, 0);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        var code = GetCodeBytes(request);
        Assert.Contains(WasmOpcodes.MemoryCopy, code);
        Assert.Contains(WasmOpcodes.I32Subtract, code);
        Assert.Contains(WasmOpcodes.I32GreaterThanSigned, code);
        Assert.Contains(target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Multiply
            : WasmOpcodes.I32Multiply, code);
        Assert.Equal(
            target == WasmTarget.Wasm64,
            code.Contains(WasmOpcodes.I64ExtendI32Unsigned));
    }

    [Fact]
    public void UnsupportedStringConstructorHasDeterministicDiagnostic()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.ManagedReference);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.ManagedReference]);

        var exception = Assert.Throws<CompilerException>(() => emitter.Emit(
            request,
            GetCodeWriter(request),
            signature,
            0));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("System.String constructor", exception.Message);
    }

    [Fact]
    public void NonCharacterArrayConstructorHasDeterministicDiagnostic()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(
            CliTypeIdentity.Primitive("void", CliValueKind.Void),
            CliTypeIdentity.SzArray(
                CliTypeIdentity.Primitive("i4", CliValueKind.I4)));
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.ManagedReference]);

        var exception = Assert.Throws<CompilerException>(() => emitter.Emit(
            request,
            GetCodeWriter(request),
            signature,
            0));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }

    [Fact]
    public void ParameterlessStringConstructorHasDeterministicDiagnostic()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var request = CreateInstructionRequest(CilOperation.NewObject);

        var exception = Assert.Throws<CompilerException>(() => emitter.Emit(
            request,
            GetCodeWriter(request),
            signature,
            0));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }

    [Fact]
    public void UnknownConstructionSourceKindFailsDeterministically()
    {
        var layouts = new RecordingLayoutProvider();
        var plans = new FixedStringConstructionPlanResolver(new StringConstructionPlan(
            (StringConstructionSourceKind)int.MaxValue,
            null,
            null));
        var emitter = CreateEmitter(layouts, plans);
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.I4,
            CliValueKind.I4);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.I4, CliValueKind.I4]);

        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            request,
            GetCodeWriter(request),
            signature,
            0));
    }

    private static IStringConstructionEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IStringConstructionPlanResolver? plans = null)
    {
        IAddressInstructionEmitter addresses = new AddressInstructionEmitter(layouts);
        IImplicitExceptionEmitter exceptions = new ImplicitExceptionEmitter(
            layouts,
            layouts,
            7);
        var program = new FakeProgram();
        return Assert.IsAssignableFrom<IStringConstructionEmitter>(new StringConstructionEmitter(
            program,
            program,
            layouts,
            addresses,
            layouts,
            layouts,
            layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            exceptions,
            plans ?? new StringConstructionPlanResolver()));
    }

    private sealed class FixedStringConstructionPlanResolver(
        StringConstructionPlan plan) : IStringConstructionPlanResolver
    {
        public StringConstructionPlan Resolve(
            MethodSignatureModel signature,
            int instructionOffset) => plan;
    }
}
