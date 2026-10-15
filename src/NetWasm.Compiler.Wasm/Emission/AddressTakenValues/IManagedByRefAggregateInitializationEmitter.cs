using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal interface IManagedByRefAggregateInitializationEmitter
{
    void Emit(IWasmInstructionWriter code, MethodEmissionContext context);
}
