using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSImportResolveEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IGeneratedFunctionWriterFactory writers,
    IHostCallbackStringArgumentMarshaller stringResults,
    IHostCallbackByteArrayArgumentMarshaller valueArrayResults,
    IHostCallbackHostObjectArgumentMarshaller hostObjectResults) : IAsyncJSImportResolveEmitter
{
    public byte[] Emit(
        JavaScriptAsyncMethodBinding binding,
        IFunctionIndexResolver functionIndices,
        InteropImportPlan interopImports)
    {
        var code = writers.Create();
        var result = binding.Return.ResultType;
        // A string, array, or JSObject result arrives as a host handle in the
        // second parameter and becomes the managed value passed to SetResult.
        var referenceResult = result is not null && (InteropTypeClassifier.IsString(result)
            || InteropTypeClassifier.IsValueArray(result) || InteropTypeClassifier.IsJSObject(result));
        var taskLocal = result is null ? 1 : 2;
        var valueLocal = taskLocal + 1;
        var temporaryI4 = valueLocal + 1;
        var objectTemporary = temporaryI4 + 1;
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [
                CliValueKind.ManagedReference,
                .. (referenceResult
                    ? [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.ManagedReference]
                    : Array.Empty<CliValueKind>()),
            ],
            layouts.Target);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.HandleGet))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)taskLocal)));
        if (referenceResult)
        {
            var target = new InteropMarshallingTarget(interopImports);
            if (InteropTypeClassifier.IsString(result!))
            {
                stringResults.Emit(code.Instructions, 1, valueLocal, temporaryI4, objectTemporary, target);
            }
            else if (InteropTypeClassifier.IsValueArray(result!))
            {
                valueArrayResults.Emit(code.Instructions, result!, 1, valueLocal, temporaryI4,
                    objectTemporary, target);
            }
            else
            {
                hostObjectResults.Emit(code.Instructions, result!, 1, valueLocal);
            }
        }
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)taskLocal)));
        if (result is not null)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)(referenceResult ? valueLocal : 1))));
        }
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                binding.SetResult))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.HandleRelease))));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
