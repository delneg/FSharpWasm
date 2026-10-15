using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IArrayIndexAdapter
{
    int Adapt(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int arrayLocal,
        int stackSlot);
}
