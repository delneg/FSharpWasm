using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumTypeCodeEmitter
{
    void EmitTypeCode(
        IWasmInstructionWriter code,
        int receiver,
        int result,
        int temporaryI4,
        OptionalFunctionIndex throwFunction,
        CliTypeIdentity? constrainedType = null,
        CliValueKind receiverKind = CliValueKind.ManagedReference);
}
