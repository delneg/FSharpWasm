using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayMetadataSnapshotEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LoopEntryUsesExistingNullSafeSnapshotContract(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = new[] { new ArrayMetadataSnapshotEmitter(layouts, layouts, CreateAddressInstructions(layouts),
                new ImplicitExceptionEmitter(layouts, layouts, 7)) }
            .Cast<IArrayMetadataSnapshotEmitter>().Single();
        var slot = new ArrayReferenceSlot(false, 0);
        var plan = new ArrayMetadataCachingPlan([slot], [])
        {
            LoopEntries = ImmutableDictionary<StructuredBlockId, ImmutableArray<ArrayReferenceSlot>>.Empty
                .Add(new(7), [slot]),
        };
        var context = CreateMethodEmissionContext() with
        {
            ArrayMetadata = new(plan,
                ImmutableDictionary<ArrayReferenceSlot, ArrayMetadataSnapshotLocals>.Empty.Add(slot, new(30, 31))),
        };
        var expected = new RecordingInstructionWriter();
        var actual = new RecordingInstructionWriter();

        emitter.Emit(expected, context, slot);
        emitter.Emit(actual, context, new StructuredBlockId(7));

        Assert.Equal(expected.ToArray(), actual.ToArray());
        Assert.Single(actual.ToInstructions(), instruction => instruction.Opcode == WasmOpcodes.If);
        Assert.Contains(actual.ToInstructions(), instruction => instruction.Opcode ==
            (target == WasmTarget.Wasm32 ? WasmOpcodes.I32Load : WasmOpcodes.I64Load));
        var absent = new RecordingInstructionWriter();
        emitter.Emit(absent, context, new StructuredBlockId(8));
        Assert.Empty(absent.ToInstructions());
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    public void LoopEntryChecksItsHoistedArraysAfterTheSnapshots(WasmTarget target, bool argument)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = new ArrayMetadataSnapshotEmitter(layouts, layouts, CreateAddressInstructions(layouts),
            new ImplicitExceptionEmitter(layouts, layouts, 7));
        var slot = new ArrayReferenceSlot(argument, 0);
        var plan = new ArrayMetadataCachingPlan([slot], [])
        {
            LoopEntries = ImmutableDictionary<StructuredBlockId, ImmutableArray<ArrayReferenceSlot>>.Empty.Add(new(7), [slot]),
            EntryNullChecks = ImmutableDictionary<StructuredBlockId, ImmutableArray<ArrayReferenceSlot>>.Empty.Add(new(7), [slot]),
        };
        var context = CreateMethodEmissionContext() with
        {
            ArrayMetadata = new(plan,
                ImmutableDictionary<ArrayReferenceSlot, ArrayMetadataSnapshotLocals>.Empty.Add(slot, new(30, 31))),
        };
        var snapshot = new RecordingInstructionWriter();
        var entry = new RecordingInstructionWriter();

        emitter.Emit(snapshot, context, slot);
        emitter.Emit(entry, context, new StructuredBlockId(7));

        var instructions = entry.ToInstructions();
        var check = instructions.Skip(snapshot.ToInstructions().Length).ToArray();
        Assert.Equal(snapshot.ToInstructions().Select(instruction => instruction.Opcode),
            instructions.Take(snapshot.ToInstructions().Length).Select(instruction => instruction.Opcode));
        Assert.Equal(WasmOpcodes.LocalGet, check[0].Opcode);
        Assert.Equal((uint)(argument ? context.ParameterOffset : context.LocalBase), check[0].Operand.UnsignedValue);
        Assert.Equal(WasmOpcodes.If, check[2].Opcode);
        Assert.Equal(WasmOpcodes.End, check[^1].Opcode);
        Assert.Contains(check, instruction => instruction.Opcode == WasmOpcodes.Throw);
    }
}
