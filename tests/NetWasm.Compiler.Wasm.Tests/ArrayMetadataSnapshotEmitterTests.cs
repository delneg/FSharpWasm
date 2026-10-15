using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
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
        var emitter = new[] { new ArrayMetadataSnapshotEmitter(layouts, layouts, CreateAddressInstructions(layouts)) }
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
}
