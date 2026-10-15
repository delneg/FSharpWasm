using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.CallLifetimes;

internal enum ScalarCallPreservationPhase
{
    BeforeCall,
    AfterNormalReturn,
}

internal interface IScalarCallPreservationEmitter
{
    void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, ScalarCallPreservationPhase phase);
}
