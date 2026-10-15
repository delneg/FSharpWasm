using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ReRegisterForFinalizeIntrinsicEmitter(
    IRuntimeImportResolver runtimeImports) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!request.Instruction.Target.RuntimeImportSelection.IncludeFinalization)
        {
            return;
        }

        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.ManagedReference)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(
            RuntimeImportSymbol.ReRegisterForFinalize,
            request.Instruction.Target.RuntimeImportSelection)))));
    }
}
