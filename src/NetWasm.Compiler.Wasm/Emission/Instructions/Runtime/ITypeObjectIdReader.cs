using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface ITypeObjectIdReader
{
    void Read(
        IWasmInstructionWriter code,
        int typeLocal,
        int typeIdLocal,
        OptionalFunctionIndex throwFunction);
}
