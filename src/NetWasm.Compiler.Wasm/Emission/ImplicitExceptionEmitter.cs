using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ImplicitExceptionEmitter(
    ITargetLayout layouts,
    IManagedExceptionObjectProvider exceptions) : IImplicitExceptionEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        ManagedExceptionKind kind,
        OptionalFunctionIndex throwFunctionIndex)
    {
        ArgumentNullException.ThrowIfNull(code);
        var functionIndex = throwFunctionIndex.Value;
        var exception = exceptions.GetExceptionObject(kind);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(exception)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(exception)));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndex)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
    }
}
