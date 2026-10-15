using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NativeMemoryIntrinsicEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearEmitsZeroValuedBulkMemoryFill(bool memory64)
    {
        var request = CreateBulkRequest(
            RuntimeIntrinsic.NativeMemoryClear,
            memory64,
            [CliValueKind.NativeInt, CliValueKind.NativeInt]);
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.NativeMemoryClear,
            WasmRuntimeImports.CreateCatalog());
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.I32Constant,
                WasmOpcodes.LocalGet, WasmOpcodes.Prefixed],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.NativeInt),
            instructions[0].Operand.UnsignedValue);
        Assert.Equal(WasmInstructionOperand.Signed(0), instructions[1].Operand);
        Assert.Equal(
            (uint)request.Local(1, CliValueKind.NativeInt),
            instructions[2].Operand.UnsignedValue);
        Assert.Equal(
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0),
            instructions[3].Operand);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyEmitsDestinationSourceAndCountBulkMemoryCopy(bool memory64)
    {
        var request = CreateBulkRequest(
            RuntimeIntrinsic.NativeMemoryCopy,
            memory64,
            [CliValueKind.NativeInt, CliValueKind.NativeInt,
                CliValueKind.NativeInt]);
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.NativeMemoryCopy,
            WasmRuntimeImports.CreateCatalog());
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.LocalGet,
                WasmOpcodes.LocalGet, WasmOpcodes.Prefixed],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)request.Local(1, CliValueKind.NativeInt),
            instructions[0].Operand.UnsignedValue);
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.NativeInt),
            instructions[1].Operand.UnsignedValue);
        Assert.Equal(
            (uint)request.Local(2, CliValueKind.NativeInt),
            instructions[2].Operand.UnsignedValue);
        Assert.Equal(
            WasmInstructionOperand.PrefixedTriple(
                WasmOpcodes.MemoryCopy,
                0,
                0),
            instructions[3].Operand);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FillEmitsPointerValueAndCountBulkMemoryFill(bool memory64)
    {
        var request = CreateBulkRequest(
            RuntimeIntrinsic.NativeMemoryFill,
            memory64,
            [CliValueKind.NativeInt, CliValueKind.NativeInt, CliValueKind.I4]);
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.NativeMemoryFill,
            WasmRuntimeImports.CreateCatalog());
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.LocalGet,
                WasmOpcodes.LocalGet, WasmOpcodes.Prefixed],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.NativeInt),
            instructions[0].Operand.UnsignedValue);
        Assert.Equal(
            (uint)request.Local(2, CliValueKind.I4),
            instructions[1].Operand.UnsignedValue);
        Assert.Equal(
            (uint)request.Local(1, CliValueKind.NativeInt),
            instructions[2].Operand.UnsignedValue);
        Assert.Equal(
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0),
            instructions[3].Operand);
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.NativeMemoryAlloc, (int)RuntimeImportSymbol.NativeAlloc, 1)]
    [InlineData(RuntimeIntrinsic.NativeMemoryRealloc, (int)RuntimeImportSymbol.NativeRealloc, 2)]
    [InlineData(RuntimeIntrinsic.NativeMemoryAlignedAlloc, (int)RuntimeImportSymbol.NativeAlignedAlloc, 2)]
    [InlineData(RuntimeIntrinsic.NativeMemoryAlignedRealloc, (int)RuntimeImportSymbol.NativeAlignedRealloc, 3)]
    public void AllocationStrategiesDelegateTheirExactRuntimeContract(
        RuntimeIntrinsic intrinsic,
        int symbolValue,
        int argumentCount)
    {
        var allocations = new RecordingAllocationEmitter();
        var request = CreateRuntimeIntrinsicRequest(
            intrinsic,
            Enumerable.Repeat(CliValueKind.NativeInt, argumentCount));
        var emitter = CreateAllocationStrategy(intrinsic, allocations);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        Assert.Equal((RuntimeImportSymbol)symbolValue, allocations.Symbol);
        Assert.Equal(argumentCount, allocations.ArgumentCount);
        Assert.Same(request, allocations.Request);
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.NativeMemoryFree, (int)RuntimeImportSymbol.NativeFree)]
    [InlineData(RuntimeIntrinsic.NativeMemoryAlignedFree, (int)RuntimeImportSymbol.NativeAlignedFree)]
    public void ReleaseStrategiesDelegateTheirExactRuntimeContract(
        RuntimeIntrinsic intrinsic,
        int symbolValue)
    {
        var releases = new RecordingReleaseEmitter();
        var request = CreateRuntimeIntrinsicRequest(
            intrinsic,
            [CliValueKind.NativeInt]);
        IRuntimeIntrinsicEmitter emitter = intrinsic == RuntimeIntrinsic.NativeMemoryFree
            ? new NativeMemoryFreeIntrinsicEmitter(releases)
            : new NativeMemoryAlignedFreeIntrinsicEmitter(releases);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        Assert.Equal((RuntimeImportSymbol)symbolValue, releases.Symbol);
        Assert.Same(request, releases.Request);
    }

    [Theory]
    [InlineData(1, (int)RuntimeImportSymbol.NativeAlloc)]
    [InlineData(3, (int)RuntimeImportSymbol.NativeAlignedRealloc)]
    public void AllocationCapabilityCallsRuntimeAndChecksForOutOfMemory(
        int argumentCount,
        int symbolValue)
    {
        var addresses = new RecordingAddressEmitter();
        var exceptions = new RecordingExceptionEmitter();
        var emitter = ThroughAllocationContract(new NativeMemoryAllocationEmitter(
            WasmRuntimeImports.CreateCatalog(),
            addresses,
            exceptions));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.NativeMemoryAlloc,
            Enumerable.Repeat(CliValueKind.NativeInt, argumentCount));

        emitter.Emit(
            request,
            GetCodeWriter(request.Instruction),
            (RuntimeImportSymbol)symbolValue,
            argumentCount);

        Assert.Equal([AddressOperation.EqualZero], addresses.Operations);
        Assert.Equal([ManagedExceptionKind.OutOfMemory], exceptions.Kinds);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void ReleaseCapabilityCallsRuntime()
    {
        var emitter = ThroughReleaseContract(new NativeMemoryReleaseEmitter(
            WasmRuntimeImports.CreateCatalog()));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.NativeMemoryFree,
            [CliValueKind.NativeInt]);

        emitter.Emit(
            request,
            GetCodeWriter(request.Instruction),
            RuntimeImportSymbol.NativeFree);

        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request.Instruction));
    }

    private static INativeMemoryAllocationEmitter ThroughAllocationContract(
        INativeMemoryAllocationEmitter emitter) => emitter;

    private static RuntimeIntrinsicEmissionRequest CreateBulkRequest(
        RuntimeIntrinsic intrinsic,
        bool memory64,
        IReadOnlyList<CliValueKind> arguments)
    {
        var request = CreateRuntimeIntrinsicRequest(intrinsic, arguments);
        return request with
        {
            Target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32,
        };
    }

    private static INativeMemoryReleaseEmitter ThroughReleaseContract(
        INativeMemoryReleaseEmitter emitter) => emitter;

    private static IRuntimeIntrinsicEmitter CreateAllocationStrategy(
        RuntimeIntrinsic intrinsic,
        INativeMemoryAllocationEmitter allocations) => intrinsic switch
        {
            RuntimeIntrinsic.NativeMemoryAlloc =>
                new NativeMemoryAllocIntrinsicEmitter(allocations),
            RuntimeIntrinsic.NativeMemoryRealloc =>
                new NativeMemoryReallocIntrinsicEmitter(allocations),
            RuntimeIntrinsic.NativeMemoryAlignedAlloc =>
                new NativeMemoryAlignedAllocIntrinsicEmitter(allocations),
            RuntimeIntrinsic.NativeMemoryAlignedRealloc =>
                new NativeMemoryAlignedReallocIntrinsicEmitter(allocations),
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };

    private sealed class RecordingAllocationEmitter : INativeMemoryAllocationEmitter
    {
        public RuntimeIntrinsicEmissionRequest? Request { get; private set; }
        public RuntimeImportSymbol? Symbol { get; private set; }
        public int? ArgumentCount { get; private set; }

        public void Emit(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code,
            RuntimeImportSymbol symbol,
            int argumentCount)
        {
            Request = request;
            Symbol = symbol;
            ArgumentCount = argumentCount;
        }
    }

    private sealed class RecordingReleaseEmitter : INativeMemoryReleaseEmitter
    {
        public RuntimeIntrinsicEmissionRequest? Request { get; private set; }
        public RuntimeImportSymbol? Symbol { get; private set; }

        public void Emit(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code,
            RuntimeImportSymbol symbol)
        {
            Request = request;
            Symbol = symbol;
        }
    }

    private sealed class RecordingAddressEmitter : IAddressInstructionEmitter
    {
        public List<AddressOperation> Operations { get; } = [];

        public void Emit(IWasmInstructionWriter code, int constant) =>
            throw new NotSupportedException();

        public void Emit(IWasmInstructionWriter code, AddressOperation operation) =>
            Operations.Add(operation);
    }

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind) =>
            Kinds.Add(kind);
    }
}
