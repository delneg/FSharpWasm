using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JSImportInvocationEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IJavaScriptResultDescriptorSizeResolver resultDescriptorSizes,
    IJavaScriptImportArgumentEmitter arguments,
    IHostCallbackHandleEmitter callbackHandles,
    IInteropHandleReleaser interopHandles,
    IJavaScriptImportResultEmitter results,
    IImplicitExceptionEmitter exceptions) : IJSImportInvocationEmitter
{
    public void Emit(
        JSImportInvocation invocation,
        IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var context = invocation.Context;
        var signature = invocation.Method.Signature;
        ManagedMemoryEmitter.EmitAddressConstant(
            code,
            layouts.Target,
            resultDescriptorSizes.Resolve(signature.ReturnSignatureType));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        HostCallbackDeclaration? callback = null;
        for (var index = 0; index < invocation.Consumed; index++)
        {
            var local = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                context.StackLocals,
                invocation.ArgumentBase + index,
                invocation.Stack[invocation.ArgumentBase + index],
                layouts.Target);
            if (invocation.Target.HostCallbacks.TryGetValue(
                    (invocation.Method.Definition.Key, index),
                    out var value))
            {
                callback = value;
                callbackHandles.Emit(code, local, context);
            }
            else
            {
                arguments.Emit(
                    code,
                    signature.ParameterSignatureTypes[index],
                    local,
                    context);
            }
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(functionIndices.Resolve(new InteropImportKey(invocation.Method.Definition.Key))))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        if (callback is not null)
        {
            interopHandles.Release(code, context);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave)))));
        exceptions.Emit(code, ManagedExceptionKind.JSException);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        results.Emit(new(
            signature,
            invocation.Stack,
            context,
            invocation.ArgumentBase,
            invocation.Consumed,
            callback,
            new InteropMarshallingTarget(invocation.Target.InteropImports)), code);
    }
}
