using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal interface IAddressTakenAggregateProjectionEmitter
{
    bool TryEmit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code);
}
