using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumHasFlagEmitter
{
    void EmitHasFlag(
        IWasmInstructionWriter code,
        int receiver,
        int flag,
        int result,
        int temporaryI4,
        OptionalFunctionIndex throwFunction,
        CliValueKind receiverKind = CliValueKind.ManagedReference,
        CliTypeIdentity? constrainedType = null);
}
