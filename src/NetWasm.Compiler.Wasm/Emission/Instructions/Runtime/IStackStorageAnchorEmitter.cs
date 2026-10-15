using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IStackStorageAnchorEmitter
{
    void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code);
}
