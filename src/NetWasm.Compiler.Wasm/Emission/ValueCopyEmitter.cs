using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IValueCopyEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        int destinationLocal,
        int size,
        int alignment,
        Action<IWasmInstructionWriter> emitSource);
}

internal sealed class BulkValueCopyEmitter(ITargetLayout layouts) : IValueCopyEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        int destinationLocal,
        int size,
        int alignment,
        Action<IWasmInstructionWriter> emitSource)
    {
        ValueCopyContract.Validate(
            code, destinationLocal, size, alignment, emitSource);
        emitSource(code);
        ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(
                WasmOpcodes.MemoryCopy, 0, 0)));
    }
}

internal sealed class SmallValueCopyEmitter(
    IValueCopyEmitter bulk) : IValueCopyEmitter
{
    private const int InlineSize = 16;

    public void Emit(
        IWasmInstructionWriter code,
        int destinationLocal,
        int size,
        int alignment,
        Action<IWasmInstructionWriter> emitSource)
    {
        ValueCopyContract.Validate(
            code, destinationLocal, size, alignment, emitSource);
        if (size != InlineSize)
        {
            bulk.Emit(code, destinationLocal, size, alignment, emitSource);
            return;
        }

        for (var offset = 0; offset < InlineSize; offset += sizeof(long))
        {
            if (offset != 0)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)destinationLocal)));
            }
            emitSource(code);
            EmitChunk(code, offset, alignment);
        }
    }

    private static void EmitChunk(
        IWasmInstructionWriter code,
        int offset,
        int alignment)
    {
        var encodedAlignment = GetEncodedAlignment(alignment);
        var operand = WasmInstructionOperand.Memory(
            encodedAlignment, (uint)offset);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Load, operand));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Store, operand));
    }

    private static uint GetEncodedAlignment(int alignment)
    {
        return alignment switch
        {
            >= sizeof(long) => 3,
            >= sizeof(int) => 2,
            >= sizeof(short) => 1,
            _ => 0,
        };
    }
}

internal static class ValueCopyContract
{
    public static void Validate(
        IWasmInstructionWriter code,
        int destinationLocal,
        int size,
        int alignment,
        Action<IWasmInstructionWriter> emitSource)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationLocal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (alignment <= 0 || alignment > 16 ||
            (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }
        ArgumentNullException.ThrowIfNull(emitSource);
    }
}
