using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class FloatingCopySignIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Call.Consumed != 2)
        {
            throw new InvalidOperationException("CopySign requires two floating-point arguments.");
        }

        var kind = request.Instruction.Stack[request.ArgumentBase];
        if (kind is not (CliValueKind.F4 or CliValueKind.F8) ||
            request.Instruction.Stack[request.ArgumentBase + 1] != kind)
        {
            throw new InvalidOperationException("CopySign requires matching floating-point arguments.");
        }

        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, kind))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Local(1, kind))));
        code.Write(WasmInstruction.NoOperand(
            kind == CliValueKind.F4 ? WasmOpcodes.F32CopySign : WasmOpcodes.F64CopySign));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, kind))));
    }
}
