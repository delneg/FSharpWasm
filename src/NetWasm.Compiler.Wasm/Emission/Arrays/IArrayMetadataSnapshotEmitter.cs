using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal interface IArrayMetadataSnapshotEmitter
{
    void Emit(IWasmInstructionWriter code, MethodEmissionContext context);

    void Emit(IWasmInstructionWriter code, MethodEmissionContext context, ArrayReferenceSlot slot);

    void Emit(IWasmInstructionWriter code, MethodEmissionContext context, StructuredBlockId loopCondition);
}
