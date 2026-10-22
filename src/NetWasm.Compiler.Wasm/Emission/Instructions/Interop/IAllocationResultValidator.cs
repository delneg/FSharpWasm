using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;
namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IAllocationResultValidator
{
    void Validate(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction);
}
