using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IBoxedValueTypeValidator
{
    void Validate(
        IWasmInstructionWriter code,
        int objectLocal,
        CliTypeIdentity targetType,
        OptionalFunctionIndex throwFunction,
        ManagedExceptionKind mismatchException = ManagedExceptionKind.InvalidCast);
}
