using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumNullCheckEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction);
}
