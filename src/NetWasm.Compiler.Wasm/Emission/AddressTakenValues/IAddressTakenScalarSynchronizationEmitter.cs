using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal interface IAddressTakenScalarSynchronizationEmitter
{
    void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        AddressTakenScalarCallSynchronization synchronization,
        AddressTakenValueSynchronizationPhase phase);
}
