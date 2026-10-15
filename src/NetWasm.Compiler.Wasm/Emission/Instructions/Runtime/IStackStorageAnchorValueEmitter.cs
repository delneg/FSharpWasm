using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IStackStorageAnchorValueEmitter
{
    void Emit(IWasmInstructionWriter code, MethodEmissionContext context, int anchor);
}
