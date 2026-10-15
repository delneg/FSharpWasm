using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class ManagedThrowFunctionAppender(
    IGeneratedFunctionWriterFactory writers,
    IRuntimeImportResolver runtimeImports) : IManagedThrowFunctionAppender
{
    private readonly IGeneratedFunctionWriterFactory _writers = writers ??
        throw new ArgumentNullException(nameof(writers));
    private readonly int _beginThrowImportIndex = (runtimeImports ??
        throw new ArgumentNullException(nameof(runtimeImports))).Resolve(
            RuntimeImportSymbol.BeginThrow);

    public void Append(
        IList<WasmFunctionDefinition> functions,
        OptionalFunctionIndex functionIndex)
    {
        ArgumentNullException.ThrowIfNull(functions);
        if (!functionIndex.IsPresent)
        {
            return;
        }

        var code = _writers.Create();
        code.Bytes.Write([0]);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)_beginThrowImportIndex)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        functions.Add(new(
            "managed.throw",
            WasmFunctionType.Create(
                CliValueKind.Void,
                CliValueKind.ManagedReference),
            code.Snapshots.Read())
        {
            IsNoInlining = true,
        });
    }
}
