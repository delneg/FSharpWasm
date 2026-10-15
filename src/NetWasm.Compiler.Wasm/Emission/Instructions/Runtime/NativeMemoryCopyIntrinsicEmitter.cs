using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NativeMemoryCopyIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        WriteLocalGet(code, request.Local(1, CliValueKind.NativeInt));
        WriteLocalGet(code, request.Local(0, CliValueKind.NativeInt));
        WriteLocalGet(code, request.Local(2, CliValueKind.NativeInt));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
    }

    private static void WriteLocalGet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
