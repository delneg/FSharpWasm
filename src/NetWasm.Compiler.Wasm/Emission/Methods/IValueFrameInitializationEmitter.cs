using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IValueFrameInitializationEmitter
{
    void Initialize(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        MethodEmissionContext context);
}
