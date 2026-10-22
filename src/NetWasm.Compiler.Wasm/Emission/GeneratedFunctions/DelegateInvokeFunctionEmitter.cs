using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record DelegateInvokeTarget(
    ImmutableArray<ManagedDelegateBinding> Bindings,
    ModuleDataPlan ModuleData);

internal sealed class DelegateInvokeFunctionEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IManagedMethodFunctionTypeResolver functionTypes,
    IDelegateInvocationTargetSelector targets,
    IGeneratedFunctionWriterFactory writers,
    IStaticInitializationEmitter initialization,
    IMethodInitializationTriggerClassifier initializationTriggers) : IDelegateInvokeFunctionEmitter
{
    public byte[] Emit(
        MethodInstanceModel invoke,
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices)
    {
        var functionType = functionTypes.Resolve(invoke);
        var parameterCount = functionType.Parameters.Length;
        var resultLocal = parameterCount;
        var rootFrameLocal = resultLocal + 1;
        var invocationListLocal = rootFrameLocal + 1;
        var invocationCountLocal = invocationListLocal + 1;
        var invocationIndexLocal = invocationCountLocal + 1;
        var currentDelegateLocal = invocationIndexLocal + 1;
        var returnsValue = invoke.Signature.ReturnSignatureType.StackKind ==
            CliValueKind.ValueType;
        var receiverParameter = returnsValue ? 1 : 0;
        var rootSources = new List<(int Parameter, int Offset)> { (receiverParameter, -1) };
        for (var index = 0; index < invoke.Signature.ParameterSignatureTypes.Length; index++)
        {
            var type = invoke.Signature.ParameterSignatureTypes[index];
            var parameter = receiverParameter + 1 + index;
            if (type.StackKind == CliValueKind.ManagedReference)
            {
                rootSources.Add((parameter, -1));
            }
            else if (type.StackKind == CliValueKind.ValueType)
            {
                foreach (var offset in values.GetValueLayout(type).ReferenceOffsets)
                {
                    rootSources.Add((parameter, offset));
                }
            }
        }

        var output = writers.Create();
        var code = output.Instructions;
        var resultType = !returnsValue && invoke.Signature.ReturnType != CliValueKind.Void
            ? invoke.Signature.ReturnType
            : CliValueKind.I4;
        WasmLocalDeclarationWriter.Write(
            output.Bytes,
            [
                resultType,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
            ],
            layouts.Target);
        WriteI32Constant(code, rootSources.Count);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        WriteLocalSet(code, rootFrameLocal);
        for (var slot = 0; slot < rootSources.Count; slot++)
        {
            (var parameter, var offset) = rootSources[slot];
            WriteLocalGet(code, rootFrameLocal);
            if (slot != 0)
            {
                EmitAddressConstant(code, slot * layouts.Target.ObjectReferenceSize);
                EmitAddressAdd(code);
            }
            WriteLocalGet(code, parameter);
            if (offset >= 0)
            {
                EmitReferenceLoad(
                    code,
                    offset);
            }
            EmitReferenceStore(code, 0);
        }

        EmitDelegateInvokeHelperCore(
            code,
            invoke,
            receiverParameter,
            resultLocal,
            returnsValue,
            invocationListLocal,
            invocationCountLocal,
            invocationIndexLocal,
            currentDelegateLocal,
            targetProgram,
            functionIndices, functionIndices.ManagedThrowFunction);

        // An exception leaves the root frame in place for its catcher to rewind.
        WriteLocalGet(code, rootFrameLocal);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave));
        if (!returnsValue && invoke.Signature.ReturnType != CliValueKind.Void)
        {
            WriteLocalGet(code, resultLocal);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return output.Snapshots.Read();
    }

    private void EmitDelegateInvokeHelperCore(
        IWasmInstructionWriter code,
        MethodInstanceModel invoke,
        int receiverParameter,
        int resultLocal,
        bool returnsValue,
        int invocationListLocal,
        int invocationCountLocal,
        int invocationIndexLocal,
        int currentDelegateLocal,
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices,
        OptionalFunctionIndex throwFunction)
    {
        WriteLocalGet(code, receiverParameter);
        EmitReferenceLoad(code, objects.DelegateInvocationListOffset);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalTee,
            WasmInstructionOperand.Unsigned((uint)invocationListLocal)));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32Constant(code, 1);
        WriteLocalSet(code, invocationCountLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteLocalGet(code, receiverParameter);
        WriteLoad(code, WasmOpcodes.I32Load, 2, objects.DelegateInvocationCountOffset);
        WriteLocalSet(code, invocationCountLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        WriteI32Constant(code, 0);
        WriteLocalSet(code, invocationIndexLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteLocalGet(code, invocationIndexLocal);
        WriteLocalGet(code, invocationCountLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));

        WriteLocalGet(code, invocationListLocal);
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteLocalGet(code, receiverParameter);
        WriteLocalSet(code, currentDelegateLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code,
            layouts.Target,
            invocationListLocal,
            invocationIndexLocal,
            layouts.Target.ObjectReferenceSize);
        EmitReferenceLoad(code, 0);
        WriteLocalSet(code, currentDelegateLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        EmitLeafInvoke(
            code,
            invoke,
            currentDelegateLocal,
            resultLocal,
            returnsValue,
            targetProgram,
            functionIndices, throwFunction);
        WriteLocalGet(code, invocationIndexLocal);
        WriteI32Constant(code, 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        WriteLocalSet(code, invocationIndexLocal);
        WriteBranch(code, 0);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitLeafInvoke(
        IWasmInstructionWriter code,
        MethodInstanceModel invoke,
        int delegateLocal,
        int resultLocal,
        bool returnsValue,
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices,
        OptionalFunctionIndex throwFunction)
    {
        var compatible = targets.Select(invoke, targetProgram.Bindings);
        foreach (var binding in compatible)
        {
            var target = binding.Target;
            WriteLocalGet(code, delegateLocal);
            WriteLoad(code, WasmOpcodes.I32Load, 2, objects.DelegateMethodIdOffset);
            WriteI32Constant(code, functionIndices.Resolve(target));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            if (initializationTriggers.Classify(target.Definition))
            {
                initialization.Emit(new(target.Definition.DeclaringType,
                    target.DeclaringType, targetProgram.ModuleData, IsMethodInvocation: true),
                    code, functionIndices);
            }
            if (returnsValue)
            {
                WriteLocalGet(code, 0);
            }
            if (!target.Definition.IsStatic)
            {
                WriteLocalGet(code, delegateLocal);
                EmitReferenceLoad(code, objects.DelegateTargetOffset);
                if (target.DeclaringType.IsValueType)
                {
                    var value = values.GetValueLayout(target.DeclaringType);
                    EmitAddressConstant(code, WasmTargetLayout.Align(
                        layouts.Target.ObjectHeaderSize, value.Alignment));
                    EmitAddressAdd(code);
                }
            }
            var firstArgument = returnsValue ? 2 : 1;
            for (var index = 0; index < invoke.Signature.ParameterTypes.Length; index++)
            {
                WriteLocalGet(code, firstArgument + index);
            }
            WriteCall(code, functionIndices.Resolve(target));
            if (!returnsValue && invoke.Signature.ReturnType != CliValueKind.Void)
            {
                WriteLocalSet(code, resultLocal);
            }
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        exceptions.Emit(code, ManagedExceptionKind.InvalidCast, throwFunction);
        for (var index = 0; index < compatible.Length; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            WriteI32Constant(code, value);
        }
    }

    private void EmitAddressAdd(IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64Add
                : WasmOpcodes.I32Add));
    }

    private void EmitReferenceEqualZero(IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64EqualZero
                : WasmOpcodes.I32EqualZero));
    }

    private void EmitReferenceLoad(IWasmInstructionWriter code, int offset)
    {
        var (opcode, alignment) = layouts.Target.UsesMemory64
            ? (WasmOpcodes.I64Load, 3u)
            : (WasmOpcodes.I32Load, 2u);
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Memory(
                alignment,
                checked((uint)offset))));
    }

    private void EmitReferenceStore(IWasmInstructionWriter code, int offset)
    {
        var (opcode, alignment) = layouts.Target.UsesMemory64
            ? (WasmOpcodes.I64Store, 3u)
            : (WasmOpcodes.I32Store, 2u);
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Memory(
                alignment,
                checked((uint)offset))));
    }

    private static void WriteLoad(
        IWasmInstructionWriter code,
        byte opcode,
        uint alignment,
        int offset) =>
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Memory(alignment, checked((uint)offset))));

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteLocalSet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteI32Constant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void WriteCall(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteBranch(IWasmInstructionWriter code, int depth) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(checked((uint)depth))));
}
