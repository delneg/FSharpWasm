using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class StringConstructionEmitter(
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IValueLayoutProvider values,
    IInstanceFieldLayoutProvider instanceFields,
    ITypeLayoutProvider typeLayouts,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IStringConstructionPlanResolver plans) : IStringConstructionEmitter
{
    public void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase)
    {
        var plan = plans.Resolve(signature, request.Instruction.Offset);
        switch (plan.SourceKind)
        {
            case StringConstructionSourceKind.RepeatedCharacter:
                EmitRepeatedCharacter(request, code, signature, argumentBase);
                return;
            case StringConstructionSourceKind.CharacterArray:
                EmitCharacterArray(
                    request,
                    code,
                    signature,
                    argumentBase,
                    plan);
                return;
            case StringConstructionSourceKind.CharacterSpan:
                EmitCharacterSpan(request, code, signature, argumentBase);
                return;
            default:
                throw new InvalidOperationException(
                    $"Unknown string construction source kind '{plan.SourceKind}'.");
        }
    }

    private void EmitCharacterSpan(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase)
    {
        var spanType = signature.ParameterSignatureTypes[0];
        var spanLocal = GetStackLocal(
            request.Context,
            argumentBase,
            CliValueKind.ValueType);
        var (reference, referenceLayout, length, lengthLayout) =
            ResolveCharacterSpan(spanType);

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Write(LocalGet(spanLocal));
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            lengthLayout.Offset,
            length.FieldType,
            lengthLayout.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalTee,
            WasmInstructionOperand.Unsigned(
                (uint)request.Context.NumericTemporaryI4)));
        Allocate(request, code);

        code.Write(LocalGet(request.Context.ObjectTemporary));
        addresses.Emit(code, objects.StringDataOffset);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(LocalGet(spanLocal));
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            referenceLayout.Offset,
            reference.FieldType,
            referenceLayout.Size);
        code.Write(LocalGet(request.Context.NumericTemporaryI4));
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        addresses.Emit(code, sizeof(char));
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64Multiply
                : WasmOpcodes.I32Multiply));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(
                WasmOpcodes.MemoryCopy,
                0,
                0)));
        Publish(request, code, signature, argumentBase);
    }

    private (
        FieldInstanceModel Reference,
        FieldLayout ReferenceLayout,
        FieldInstanceModel Length,
        FieldLayout LengthLayout) ResolveCharacterSpan(CliTypeIdentity spanType)
    {
        var definition = typeDefinitions.ResolveTypeIdentity(spanType);
        var resolved = definition.Fields
            .Select(fields.GetField)
            .Select(field => new FieldInstanceModel(
                field,
                spanType,
                field.SignatureType.Substitute(spanType.TypeArguments)))
            .ToArray();
        var reference = resolved.SingleOrDefault(field =>
            field.Definition.Name == "_reference");
        var length = resolved.SingleOrDefault(field =>
            field.Definition.Name == "_length");
        if (reference is null || length is null ||
            reference.FieldType.Shape != CliTypeShape.ManagedByReference ||
            reference.FieldType.ElementType!.CanonicalName != "primitive:char" ||
            length.FieldType.CanonicalName != "primitive:i4")
        {
            throw Invalid("ReadOnlySpan<char> has an incompatible field contract.");
        }

        var spanLayout = values.GetValueLayout(spanType);
        var referenceLayout = instanceFields.GetFieldLayout(reference);
        var lengthLayout = instanceFields.GetFieldLayout(length);
        if (referenceLayout.Size != layouts.Target.AddressSize ||
            lengthLayout.Size != sizeof(int) ||
            !Contains(spanLayout, referenceLayout) ||
            !Contains(spanLayout, lengthLayout) ||
            !spanLayout.ByReferenceOffsets.Contains(referenceLayout.Offset))
        {
            throw Invalid("ReadOnlySpan<char> has an incompatible value layout.");
        }

        return (reference, referenceLayout, length, lengthLayout);
    }

    private static bool Contains(ValueLayout value, FieldLayout field) =>
        field.Offset >= 0 &&
        checked(field.Offset + field.Size) <= value.Size;

    private static CompilerException Invalid(string message) => new(new(
        DiagnosticCode.RuntimeContract,
        message));

    private void EmitRepeatedCharacter(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase)
    {
        var lengthLocal = GetStackLocal(
            request.Context,
            argumentBase + 1,
            CliValueKind.I4);
        EmitArgumentOutOfRangeIfNegative(code, lengthLocal, request.Context.ManagedThrowFunction);
        code.Write(LocalGet(lengthLocal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(
                (int)((uint.MaxValue - 2 * sizeof(int)) / sizeof(char)))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, request.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(LocalGet(GetStackLocal(
            request.Context,
            argumentBase,
            request.Stack[argumentBase])));
        code.Write(LocalGet(lengthLocal));
        Allocate(request, code);
        Publish(request, code, signature, argumentBase);
    }

    private void EmitCharacterArray(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase,
        StringConstructionPlan plan)
    {
        var arrayLocal = GetStackLocal(
            request.Context,
            argumentBase,
            CliValueKind.ManagedReference);
        code.Write(LocalGet(arrayLocal));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.ArgumentNull, request.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        var startLocal = plan.StartArgumentIndex is { } startArgumentIndex
            ? GetStackLocal(
                request.Context,
                argumentBase + startArgumentIndex,
                CliValueKind.I4)
            : (int?)null;
        var lengthLocal = plan.LengthArgumentIndex is { } lengthArgumentIndex
            ? GetStackLocal(
                request.Context,
                argumentBase + lengthArgumentIndex,
                CliValueKind.I4)
            : (int?)null;
        if (startLocal is { } start)
        {
            EmitArgumentOutOfRangeIfNegative(code, start, request.Context.ManagedThrowFunction);
        }
        if (lengthLocal is { } length)
        {
            EmitArgumentOutOfRangeIfNegative(code, length, request.Context.ManagedThrowFunction);
        }
        if (startLocal is { } boundedStart)
        {
            var boundedLength = lengthLocal!.Value;
            code.Write(LocalGet(boundedStart));
            EmitArrayLength(code, arrayLocal);
            code.Write(LocalGet(boundedLength));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanSigned));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.ArgumentOutOfRange, request.Context.ManagedThrowFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        EmitCharacterCount(code, arrayLocal, lengthLocal);
        Allocate(request, code);

        code.Write(LocalGet(request.Context.ObjectTemporary));
        addresses.Emit(code, objects.StringDataOffset);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(LocalGet(arrayLocal));
        ManagedMemoryEmitter.EmitLoadBySize(
            code,
            layouts.Target,
            objects.ArrayDataPointerOffset,
            layouts.Target.AddressSize);
        if (startLocal is { } copyStart)
        {
            code.Write(LocalGet(copyStart));
            if (layouts.Target.UsesMemory64)
            {
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            }
            addresses.Emit(code, sizeof(char));
            code.Write(WasmInstruction.NoOperand(
                layouts.Target.UsesMemory64
                    ? WasmOpcodes.I64Multiply
                    : WasmOpcodes.I32Multiply));
            addresses.Emit(code, AddressOperation.Add);
        }
        EmitCharacterCount(code, arrayLocal, lengthLocal);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        addresses.Emit(code, sizeof(char));
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64 ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        Publish(request, code, signature, argumentBase);
    }

    private void EmitArgumentOutOfRangeIfNegative(
        IWasmInstructionWriter code,
        int local,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(LocalGet(local));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.ArgumentOutOfRange, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitCharacterCount(
        IWasmInstructionWriter code,
        int arrayLocal,
        int? lengthLocal)
    {
        if (lengthLocal is { } length)
        {
            code.Write(LocalGet(length));
            return;
        }
        EmitArrayLength(code, arrayLocal);
    }

    private void Allocate(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(typeLayouts.StringTypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)runtimeImports.Resolve(RuntimeImportSymbol.AllocateString))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(LocalGet(request.Context.ObjectTemporary));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, request.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void Publish(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase)
    {
        code.Write(LocalGet(request.Context.ObjectTemporary));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Context,
                argumentBase,
                CliValueKind.ManagedReference))));
        request.Stack.RemoveRange(argumentBase, signature.ParameterTypes.Length);
        request.Stack.Add(CliValueKind.ManagedReference);
    }

    private void EmitArrayLength(IWasmInstructionWriter code, int arrayLocal)
    {
        code.Write(LocalGet(arrayLocal));
        ManagedMemoryEmitter.EmitLoadBySize(
            code,
            layouts.Target,
            objects.ArrayLengthOffset,
            sizeof(int));
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);

    private static WasmInstruction LocalGet(int local) =>
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local));
}
