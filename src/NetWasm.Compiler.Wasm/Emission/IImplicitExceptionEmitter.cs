using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IImplicitExceptionEmitter
{
    void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind);

    void Emit(
        IWasmInstructionWriter code,
        ManagedExceptionKind kind,
        OptionalFunctionIndex throwFunctionIndex) => throw new NotSupportedException(
            "this exception emitter does not support a shared throw function");
}
