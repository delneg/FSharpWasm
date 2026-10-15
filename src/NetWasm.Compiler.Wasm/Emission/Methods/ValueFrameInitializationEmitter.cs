using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ValueFrameInitializationEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IValueFrameAddressEmitter addresses) : IValueFrameInitializationEmitter
{
    public void Initialize(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(context);

        EmitRange(code, context, CreateLocalRange(header, context));
        EmitRange(code, context, CreateFilterLocalRange(header, context));
        EmitRange(code, context, CreateRange(context.FilterEnvironment.AnchorOffsets.Values
            .Select(offset => new StorageRange(offset, layouts.Target.AddressSize))));
    }

    private StorageRange? CreateLocalRange(
        StructuredMethodHeader header,
        MethodEmissionContext context) => CreateRange(
        context.ValueLayout.LocalOffsets.Select(item => new StorageRange(
            item.Value,
            GetStorageSize(header.LocalSignatureTypes[item.Key]))));

    private StorageRange? CreateFilterLocalRange(
        StructuredMethodHeader header,
        MethodEmissionContext context) => CreateRange(
        context.FilterEnvironment.Captures.Values
            .Where(capture => !capture.Slot.IsArgument &&
                (!context.ValueLayout.LocalOffsets.TryGetValue(
                    capture.Slot.Index,
                    out var localOffset) ||
                    localOffset != capture.Offset))
            .Select(capture => new StorageRange(
                capture.Offset,
                GetStorageSize(header.LocalSignatureTypes[capture.Slot.Index]))));

    private int GetStorageSize(CliTypeIdentity type) =>
        type.StackKind == CliValueKind.ValueType
            ? values.GetValueLayout(type).Size
            : layouts.Target.GetStorageSize(type);

    private static StorageRange? CreateRange(IEnumerable<StorageRange> ranges)
    {
        var materialized = ranges.ToArray();
        if (materialized.Length == 0)
        {
            return null;
        }

        var start = materialized.Min(range => range.Offset);
        var end = materialized.Max(range => checked(range.Offset + range.Size));
        return new(start, checked(end - start));
    }

    private void EmitRange(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        StorageRange? range)
    {
        if (range is not { } storage)
        {
            return;
        }

        addresses.Emit(code, context, storage.Offset);
        if (storage.Size is 1 or 2 or 4 or 8)
        {
            code.Write(WasmInstruction.WithOperand(
                storage.Size == 8
                    ? WasmOpcodes.I64Constant
                    : WasmOpcodes.I32Constant,
                storage.Size == 8
                    ? WasmInstructionOperand.Signed64(0)
                    : WasmInstructionOperand.Signed(0)));
            ManagedMemoryEmitter.EmitStoreBySize(
                code,
                layouts.Target,
                0,
                storage.Size);
            return;
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        ManagedMemoryEmitter.EmitAddressConstant(
            code,
            layouts.Target,
            storage.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
    }

    private readonly record struct StorageRange(int Offset, int Size);
}
