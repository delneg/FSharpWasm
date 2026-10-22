using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IArrayReceiverValidator
{
    void Validate(
        IWasmInstructionWriter code,
        int receiverLocal,
        OptionalFunctionIndex throwFunction);
}
