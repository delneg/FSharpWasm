using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ValueCopyEmitterTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    public void FallbackReceivesUnmodifiedRequestOnlyForNonInlineCopies(int size)
    {
        var fallback = new RecordingCopyEmitter();
        var emitter = new SmallValueCopyEmitter(fallback);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var sourceCalls = 0;
        Action<IWasmInstructionWriter> source = _ => sourceCalls++;

        emitter.Emit(code, 7, size, 4, source);

        if (size == 16)
        {
            Assert.Empty(fallback.Requests);
            Assert.Equal(2, sourceCalls);
            Assert.Equal(2, code.ToInstructions().Count(IsStore));
        }
        else
        {
            var request = Assert.Single(fallback.Requests);
            Assert.Same(code, request.Code);
            Assert.Equal(7, request.Destination);
            Assert.Equal(size, request.Size);
            Assert.Equal(4, request.Alignment);
            Assert.Same(source, request.Source);
            Assert.Equal(0, sourceCalls);
            Assert.Empty(code.ToInstructions());
        }
    }

    [Theory]
    [InlineData(1, 0U)]
    [InlineData(2, 1U)]
    [InlineData(4, 2U)]
    [InlineData(8, 3U)]
    [InlineData(16, 3U)]
    public void SixteenByteCopiesUseTwoScalarChunks(
        int alignment,
        uint encodedAlignment)
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateSmall(layouts);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var sourceCalls = 0;
        WriteLocalGet(code, 7);

        emitter.Emit(code, 7, 16, alignment, writer =>
        {
            sourceCalls++;
            WriteLocalGet(writer, 8);
        });

        var instructions = code.ToInstructions();
        Assert.Equal(2, sourceCalls);
        Assert.Equal(2, instructions.Count(IsLoad));
        Assert.Equal(2, instructions.Count(IsStore));
        Assert.DoesNotContain(instructions, IsMemoryCopy);
        Assert.Equal(2,
            instructions.Count(instruction =>
                instruction.Opcode == WasmOpcodes.LocalGet &&
                instruction.Operand.UnsignedValue == 7));
        Assert.Equal(
            [0U, 0U, 8U, 8U],
            instructions
            .Where(instruction => IsLoad(instruction) || IsStore(instruction))
            .Select(instruction => instruction.Operand.Offset));
        Assert.Equal(
            [encodedAlignment, encodedAlignment, encodedAlignment,
                encodedAlignment],
            instructions
                .Where(instruction => IsLoad(instruction) ||
                    IsStore(instruction))
                .Select(instruction => instruction.Operand.Alignment));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 15)]
    [InlineData(false, 17)]
    [InlineData(true, 1)]
    [InlineData(true, 15)]
    [InlineData(true, 17)]
    public void OtherCopiesDelegateToBulkMemoryCopy(
        bool memory64,
        int size)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var emitter = CreateSmall(layouts);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var sourceCalls = 0;
        WriteLocalGet(code, 5);

        emitter.Emit(code, 5, size, 1, writer =>
        {
            sourceCalls++;
            WriteLocalGet(writer, 6);
        });

        Assert.Equal(1, sourceCalls);
        Assert.Single(code.ToInstructions(), IsMemoryCopy);
        Assert.DoesNotContain(code.ToInstructions(), IsLoad);
        Assert.DoesNotContain(code.ToInstructions(), IsStore);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == (memory64
                ? WasmOpcodes.I64Constant
                : WasmOpcodes.I32Constant));
    }

    [Fact]
    public void InvalidRequestsFailBeforeWritingOrCallingSource()
    {
        var layouts = new RecordingLayoutProvider();
        var small = CreateSmall(layouts);
        var bulk = new BulkValueCopyEmitter(layouts);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var sourceCalls = 0;
        Action<IWasmInstructionWriter> source =
            _ => sourceCalls++;

        Assert.Throws<ArgumentNullException>(() =>
            small.Emit(null!, 0, 1, 1, source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            small.Emit(code, -1, 1, 1, source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            small.Emit(code, 0, 0, 1, source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            small.Emit(code, 0, 1, 0, source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            small.Emit(code, 0, 1, 32, source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            small.Emit(code, 0, 1, 3, source));
        Assert.Throws<ArgumentNullException>(() =>
            small.Emit(code, 0, 1, 1, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            bulk.Emit(code, 0, 0, 1, source));

        Assert.Equal(0, sourceCalls);
        Assert.Empty(code.ToInstructions());
    }

    private static SmallValueCopyEmitter CreateSmall(
        RecordingLayoutProvider layouts) =>
        new SmallValueCopyEmitter(new BulkValueCopyEmitter(layouts));

    private static void WriteLocalGet(
        IWasmInstructionWriter code,
        int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static bool IsLoad(WasmInstruction instruction) =>
        instruction.Opcode is WasmOpcodes.I64Load or WasmOpcodes.I32Load or
            WasmOpcodes.I32Load16Unsigned or WasmOpcodes.I32Load8Unsigned;

    private static bool IsStore(WasmInstruction instruction) =>
        instruction.Opcode is WasmOpcodes.I64Store or WasmOpcodes.I32Store or
            WasmOpcodes.I32Store16 or WasmOpcodes.I32Store8;

    private static bool IsMemoryCopy(WasmInstruction instruction) =>
        instruction.Opcode == WasmOpcodes.Prefixed &&
        instruction.Operand.Alignment == WasmOpcodes.MemoryCopy;

    private sealed class RecordingCopyEmitter : IValueCopyEmitter
    {
        public List<(IWasmInstructionWriter Code, int Destination, int Size,
            int Alignment, Action<IWasmInstructionWriter> Source)> Requests
        { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            int destinationLocal,
            int size,
            int alignment,
            Action<IWasmInstructionWriter> emitSource) =>
            Requests.Add((code, destinationLocal, size, alignment, emitSource));
    }
}
