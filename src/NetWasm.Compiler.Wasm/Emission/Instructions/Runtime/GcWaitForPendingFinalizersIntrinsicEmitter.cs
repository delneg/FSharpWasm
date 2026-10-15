using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class GcWaitForPendingFinalizersIntrinsicEmitter(IRuntimeImportResolver runtimeImports)
    : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!request.Instruction.Target.RuntimeImportSelection.IncludeFinalization)
        {
            return;
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.GcWaitForPendingFinalizers,
                request.Instruction.Target.RuntimeImportSelection))));
    }
}
