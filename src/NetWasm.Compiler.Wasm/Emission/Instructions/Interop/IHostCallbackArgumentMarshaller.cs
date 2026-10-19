using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IHostCallbackStringArgumentMarshaller
{
    void Emit(
        IWasmInstructionWriter code,
        int handleParameter,
        int destination,
        int temporaryI4,
        int objectTemporary,
        InteropMarshallingTarget target);
}

/// <summary>Wraps a host object handle in its managed <c>JSObject</c>, which then owns it.</summary>
internal interface IHostCallbackHostObjectArgumentMarshaller
{
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity objectType,
        int handleParameter,
        int destination);
}

internal interface IHostCallbackByteArrayArgumentMarshaller
{
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity arrayType,
        int handleParameter,
        int destination,
        int temporaryI4,
        int objectTemporary,
        InteropMarshallingTarget target);
}
