using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal interface IManagedAggregateValueInitializer
{
    bool TryInitializeArgument(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        MethodEmissionContext context,
        int argumentIndex);
}
