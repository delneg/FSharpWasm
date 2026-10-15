using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StringLengthIntrinsicEmitterTests
{
    [Fact]
    public void EmitsNullFailureThroughPlannedManagedThrowFunction()
    {
        var layouts = new RecordingLayoutProvider();
        var exceptions = new RecordingExceptionEmitter();
        var request = WithThrowFunction(
            CreateRuntimeIntrinsicRequest(
                RuntimeIntrinsic.StringLength,
                [CliValueKind.ManagedReference]),
            OptionalFunctionIndex.At(43));
        var writer = new RecordingInstructionWriter();

        new StringLengthIntrinsicEmitter(
            layouts,
            exceptions,
            CreateAddressInstructions(layouts)).Emit(request, writer);

        Assert.Equal(0, exceptions.InlineCalls);
        Assert.Equal(1, exceptions.SharedCalls);
        Assert.Equal(ManagedExceptionKind.NullReference, exceptions.Kind);
        Assert.Equal(43, exceptions.FunctionIndex.Value);
        Assert.Equal(
            [
                WasmOpcodes.LocalGet,
                WasmOpcodes.I32EqualZero,
                WasmOpcodes.If,
                WasmOpcodes.End,
                WasmOpcodes.LocalGet,
                WasmOpcodes.I32Load,
                WasmOpcodes.LocalSet,
            ],
            writer.ToInstructions().Select(instruction => instruction.Opcode));
    }

    [Fact]
    public void MissingManagedThrowFunctionFailsBeforeEmission()
    {
        var layouts = new RecordingLayoutProvider();
        var exceptions = new RecordingExceptionEmitter();
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.StringLength,
            [CliValueKind.ManagedReference]);
        var writer = new RecordingInstructionWriter();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new StringLengthIntrinsicEmitter(
                layouts,
                exceptions,
                CreateAddressInstructions(layouts)).Emit(request, writer));

        Assert.Contains("managed throw function", exception.Message);
        Assert.Empty(writer.ToInstructions());
        Assert.Equal(0, exceptions.InlineCalls);
        Assert.Equal(0, exceptions.SharedCalls);
    }

    private static RuntimeIntrinsicEmissionRequest WithThrowFunction(
        RuntimeIntrinsicEmissionRequest request,
        OptionalFunctionIndex functionIndex) => request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with
                    {
                        FunctionIndices = request.Instruction.Target.FunctionIndices with
                        {
                            ManagedThrowFunctionIndex = functionIndex,
                        },
                    },
                },
            },
        };

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public int InlineCalls { get; private set; }
        public int SharedCalls { get; private set; }
        public ManagedExceptionKind Kind { get; private set; }
        public OptionalFunctionIndex FunctionIndex { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            ManagedExceptionKind kind)
        {
            InlineCalls++;
            Kind = kind;
        }

        public void Emit(
            IWasmInstructionWriter code,
            ManagedExceptionKind kind,
            OptionalFunctionIndex throwFunctionIndex)
        {
            SharedCalls++;
            Kind = kind;
            FunctionIndex = throwFunctionIndex;
        }
    }
}
