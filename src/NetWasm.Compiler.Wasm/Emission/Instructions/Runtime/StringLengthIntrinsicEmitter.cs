using System;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class StringLengthIntrinsicEmitter(
    IRuntimeObjectLayout objects,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var throwFunctionIndex = request.Instruction.Target.FunctionIndices
            .ManagedThrowFunctionIndex;
        if (!throwFunctionIndex.IsPresent)
        {
            throw new InvalidOperationException(
                "string-length emission requires the managed throw function");
        }

        var stringLocal = request.Local(0, CliValueKind.ManagedReference);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(stringLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(
            code,
            ManagedExceptionKind.NullReference,
            throwFunctionIndex);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(stringLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(objects.StringLengthOffset))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.I4)))));
    }
}
