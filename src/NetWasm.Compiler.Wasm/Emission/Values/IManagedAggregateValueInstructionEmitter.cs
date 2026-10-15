using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal interface IManagedAggregateValueInstructionEmitter
{
    bool TryEmit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code);
}
