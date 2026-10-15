using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NativeMemoryFillIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        WriteLocalGet(code, request.Local(0, CliValueKind.NativeInt));
        WriteLocalGet(code, request.Local(2, CliValueKind.I4));
        WriteLocalGet(code, request.Local(1, CliValueKind.NativeInt));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
    }

    private static void WriteLocalGet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
