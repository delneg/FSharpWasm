using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// Methods throw by calling these functions, so no ordinary method holds a throw
// instruction. managed.throw begins a fresh dispatch; managed.rethrow keeps the stack
// trace an ExceptionDispatchInfo captured.
internal sealed class ManagedThrowFunctionAppender(
    IGeneratedFunctionWriterFactory writers,
    IRuntimeImportResolver runtimeImports) : IManagedThrowFunctionAppender
{
    private readonly IGeneratedFunctionWriterFactory _writers = writers ??
        throw new ArgumentNullException(nameof(writers));
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports ??
        throw new ArgumentNullException(nameof(runtimeImports));

    public void Append(
        IList<WasmFunctionDefinition> functions,
        OptionalFunctionIndex throwFunctionIndex,
        OptionalFunctionIndex rethrowFunctionIndex)
    {
        ArgumentNullException.ThrowIfNull(functions);
        if (throwFunctionIndex.IsPresent)
        {
            functions.Add(Create("managed.throw", RuntimeImportSymbol.BeginThrow));
        }
        if (rethrowFunctionIndex.IsPresent)
        {
            functions.Add(Create("managed.rethrow", RuntimeImportSymbol.BeginRethrow));
        }
    }

    private WasmFunctionDefinition Create(string name, RuntimeImportSymbol begin)
    {
        var code = _writers.Create();
        code.Bytes.Write([0]);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)_runtimeImports.Resolve(begin))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return new(
            name,
            WasmFunctionType.Create(
                CliValueKind.Void,
                CliValueKind.ManagedReference),
            code.Snapshots.Read())
        {
            IsNoInlining = true,
        };
    }
}
