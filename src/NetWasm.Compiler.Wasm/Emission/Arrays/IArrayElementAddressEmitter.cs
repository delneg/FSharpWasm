using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal interface IArrayElementAddressEmitter
{
    void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code,
        int arrayLocal, int indexLocal, int elementSize);
}
