using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class TerminalBranchValueFrameEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    public void FramesOneArmWhilePreservingMethodRootsAndTrace(WasmTarget target, bool frameFirst)
    {
        var fixture = new Fixture(target, frameFirst);
        var bodies = new List<(StructuredSequence Sequence, MethodEmissionContext Context)>();

        fixture.Emit((sequence, context) => bodies.Add((sequence, context)));

        Assert.Equal(2, bodies.Count);
        Assert.Same(fixture.Placement.Plan.WhenTrue, bodies[0].Sequence);
        Assert.Same(fixture.Placement.Plan.WhenFalse, bodies[1].Sequence);
        var framed = bodies[frameFirst ? 0 : 1].Context;
        var unframed = bodies[frameFirst ? 1 : 0].Context;
        Assert.Same(fixture.Context, unframed);
        Assert.Same(fixture.Placement.FramedLayout, framed.ValueLayout);
        Assert.Equal(fixture.Context.RootSlotCount, framed.RootSlotCount);
        Assert.Equal(fixture.Context.StackTraceMethodId, framed.StackTraceMethodId);
        Assert.Equal(1, fixture.Frames.Calls);
        Assert.Equal(["condition", "allocate"], fixture.Events);
        // An exception leaves the branch frame for its catcher to rewind.
        Assert.DoesNotContain(fixture.Code.ToInstructions(), instruction => instruction.Opcode == WasmOpcodes.TryTable);

        var instructions = fixture.Code.ToInstructions();
        Assert.Equal(1, instructions.Count(instruction => instruction.Opcode == WasmOpcodes.If));
        Assert.Equal(1, instructions.Count(instruction => instruction.Opcode == WasmOpcodes.Else));
        var store = Assert.Single(instructions, instruction => instruction.Opcode == WasmOpcodes.I64Store);
        var index = instructions.IndexOf(store);
        Assert.Equal(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(101)), instructions[index - 2]);
        Assert.Equal(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)fixture.Context.LocalBase)), instructions[index - 1]);
        Assert.Equal(frameFirst, index < instructions.IndexOf(WasmInstruction.NoOperand(WasmOpcodes.Else)));
    }

    [Fact]
    public void AllocationEmissionFailureStopsBeforeTheBody()
    {
        var fixture = new Fixture(WasmTarget.Wasm32, true);
        fixture.Frames.Fail = true;
        var bodies = 0;

        Assert.Throws<InvalidOperationException>(() => fixture.Emit((_, _) => bodies++));

        Assert.Equal(0, bodies);
        Assert.Equal(["condition", "allocate"], fixture.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RejectsInvalidPlacementBindingsBeforeCallbacksOrOutput(int invalid)
    {
        var fixture = new Fixture(WasmTarget.Wasm32, true);
        var context = invalid switch
        {
            0 => fixture.Context with { ValueLayout = fixture.Placement.FramedLayout },
            2 => fixture.Context with { FilterEnvironment = FilterEnvironmentLayout.Empty with { Size = 8 } },
            3 => fixture.Context with { ExceptionRootSlots = ImmutableDictionary<StructuredExceptionGroupId, int>.Empty.Add(new(0), 0) },
            4 => fixture.Context with { AddressTakenScalarPromotions = AddressTakenScalarPromotionLayout.Empty },
            _ => fixture.Context,
        };
        var placement = invalid == 1 ? fixture.Placement with { FramedLayout = new(0, [], [], [], []) } : fixture.Placement;
        Assert.Throws<InvalidOperationException>(() => fixture.Emitter.Emit(fixture.Code, fixture.Header, context,
            placement, (_, _) => throw new InvalidOperationException("condition should not run"),
            (_, _) => throw new InvalidOperationException("body should not run")));
        Assert.Empty(fixture.Code.ToInstructions());
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void RejectsMissingInputsBeforeWriting()
    {
        var fixture = new Fixture(WasmTarget.Wasm32, true);
        Func<StructuredBlockOccurrence, MethodEmissionContext, ConditionValue> condition = (_, _) => new(0, CliValueKind.I4);
        Action<StructuredSequence, MethodEmissionContext> body = (_, _) => { };
        Assert.Throws<ArgumentNullException>(() => fixture.Emitter.Emit(null!, fixture.Header, fixture.Context, fixture.Placement, condition, body));
        Assert.Throws<ArgumentNullException>(() => fixture.Emitter.Emit(fixture.Code, null!, fixture.Context, fixture.Placement, condition, body));
        Assert.Throws<ArgumentNullException>(() => fixture.Emitter.Emit(fixture.Code, fixture.Header, null!, fixture.Placement, condition, body));
        Assert.Throws<ArgumentNullException>(() => fixture.Emitter.Emit(fixture.Code, fixture.Header, fixture.Context, null!, condition, body));
        Assert.Throws<ArgumentNullException>(() => fixture.Emitter.Emit(fixture.Code, fixture.Header, fixture.Context, fixture.Placement, null!, body));
        Assert.Throws<ArgumentNullException>(() => fixture.Emitter.Emit(fixture.Code, fixture.Header, fixture.Context, fixture.Placement, condition, null!));
        Assert.Empty(fixture.Code.ToInstructions());
        Assert.Empty(fixture.Events);
    }

    private sealed class Fixture
    {
        public Fixture(WasmTarget target, bool frameFirst)
        {
            var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
            Frames = new(Events);
            Emitter = new[] { new TerminalBranchValueFrameEmitter(layouts,
                new StructuredConditionValueEmitter(layouts), Frames) }
                .Cast<ITerminalBranchValueFrameEmitter>().Single();
            var type = CliTypeIdentity.FromStackKind(CliValueKind.I8);
            Header = new(new FakeProgram().GetMethod(EntryKey), null, 1, [CliValueKind.I8], [type], []);
            Context = CreateMethodEmissionContext() with
            {
                ValueLayout = new(0, [], [], [], []),
                RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(new(RootSourceKind.Argument, 0), 0), []),
                StackTraceMethodId = 17,
                AddressTakenScalarPromotions = new(new(ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, type), []),
                    ImmutableDictionary<int, int>.Empty.Add(0, 101)),
            };
            Placement = new(new(new(new(0), StructuredBlockRole.Owner),
                new([new StructuredCode(new(new(1), StructuredBlockRole.Owner))]),
                new([new StructuredCode(new(new(2), StructuredBlockRole.Owner))]), frameFirst, [0]),
                new(16, ImmutableDictionary<int, int>.Empty.Add(0, 0), [], [], [0]));
        }

        public ITerminalBranchValueFrameEmitter Emitter { get; }
        public StructuredMethodHeader Header { get; }
        public MethodEmissionContext Context { get; }
        public TerminalBranchValueFramePlacement Placement { get; }
        public RecordingInstructionWriter Code { get; } = new();
        public List<string> Events { get; } = [];
        public RecordingFrames Frames { get; }

        public void Emit(Action<StructuredSequence, MethodEmissionContext> body) => Emitter.Emit(Code, Header, Context,
            Placement, (occurrence, context) =>
            {
                Assert.Equal(Placement.Plan.Condition, occurrence);
                Assert.Same(Context, context);
                Events.Add("condition");
                return new(0, CliValueKind.I4);
            }, body);
    }

    private sealed class RecordingFrames(List<string> events) : IValueFrameEntryEmitter
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        public void Emit(IWasmInstructionWriter code, StructuredMethodHeader header, MethodEmissionContext context)
        {
            Calls++;
            events.Add("allocate");
            if (Fail) throw new InvalidOperationException("allocation emission failed");
        }
    }
}
