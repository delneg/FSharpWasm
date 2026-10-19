using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class JSImportInvocationEmitterTests
{
    private const int RawImportIndex = 40;
    private const int ForwarderIndex = 50;

    [Theory]
    [InlineData(false, 2, 0, 0)]
    [InlineData(true, 1, 1, 1)]
    public void EmitsArgumentsCallbackFailureCleanupAndResultThroughFocusedActions(
        bool hasCallback,
        int expectedArgumentCount,
        int expectedCallbackCount,
        int expectedReleaseCount)
    {
        var program = new FakeProgram();
        var method = CreateImport(program);
        var target = CreateInstructionModuleTarget(program);
        if (hasCallback)
        {
            var callbackMethod = CreateMethodInstance(program.GetMethod(EntryKey));
            target = target with
            {
                HostCallbacks = ImmutableDictionary<(EntityKey, int), HostCallbackDeclaration>
                    .Empty.Add(
                        (EntryKey, 0),
                        new HostCallbackDeclaration(EntryKey, 0, callbackMethod, "callback")),
            };
        }
        var arguments = new RecordingArgumentEmitter();
        var callbackHandles = new RecordingCallbackHandleEmitter();
        var handles = new RecordingHandleReleaser();
        var results = new RecordingResultEmitter();
        var exceptions = new RecordingExceptionEmitter();
        var emitter = Assert.IsAssignableFrom<IJSImportInvocationEmitter>(new JSImportInvocationEmitter(
            new RecordingLayoutProvider(),
            WasmRuntimeImports.CreateCatalog(),
            new FixedDescriptorSizeResolver(16),
            arguments,
            callbackHandles,
            handles,
            results,
            exceptions));
        var code = new RecordingInstructionWriter();

        emitter.Emit(
            new JSImportInvocation(
                method,
                CreateMethodEmissionContext(),
                [CliValueKind.ManagedReference, CliValueKind.I4],
                0,
                2,
                target),
            code,
            CreateResolver(program));

        Assert.Equal(expectedArgumentCount, arguments.Count);
        Assert.Equal(expectedCallbackCount, callbackHandles.Count);
        Assert.Equal(expectedReleaseCount, handles.Count);
        Assert.Equal([ManagedExceptionKind.JSException], exceptions.Kinds);
        Assert.NotNull(results.Request);
        Assert.Equal(hasCallback, results.Request.Callback is not null);
    }

    [Fact]
    public void CallsTheRawInteropImportEvenWhenAManagedForwarderExists()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = Assert.IsAssignableFrom<IJSImportInvocationEmitter>(new JSImportInvocationEmitter(
            layouts,
            runtimeImports,
            new FixedDescriptorSizeResolver(16),
            new RecordingArgumentEmitter(),
            new RecordingCallbackHandleEmitter(),
            new RecordingHandleReleaser(),
            new RecordingResultEmitter(),
            new RecordingExceptionEmitter()));
        var code = new RecordingInstructionWriter();
        var functionIndices = CreateResolver(program);

        emitter.Emit(
            new JSImportInvocation(
                CreateImport(program),
                CreateMethodEmissionContext(),
                [CliValueKind.ManagedReference, CliValueKind.I4],
                0,
                2,
                CreateInstructionModuleTarget(program)),
            code,
            functionIndices);

        var calls = code.ToInstructions()
            .Where(instruction => instruction.Opcode == WasmOpcodes.Call)
            .Select(instruction => instruction.Operand.UnsignedValue)
            .ToArray();
        Assert.Contains((uint)RawImportIndex, calls);
        Assert.DoesNotContain((uint)ForwarderIndex, calls);
        Assert.Equal(ForwarderIndex, functionIndices.Resolve(EntryKey));
    }

    private static MethodInstanceModel CreateImport(FakeProgram program) =>
        CreateMethodInstance(program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4),
            JSImport = new("run", "tests"),
        });

    private static IFunctionIndexResolver CreateResolver(FakeProgram program) =>
        CreateFunctionIndexResolver(
            program,
            new FunctionIndexMap([], [], [],
                ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(EntryKey, new(RawImportIndex)))
            {
                JSImportForwarders = ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty
                    .Add(EntryKey, new(ForwarderIndex)),
            });

    private static MethodInstanceModel CreateMethodInstance(MethodDefinitionModel method) => new(
        method,
        CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
        [],
        method.Signature);

    private sealed class FixedDescriptorSizeResolver(int size) :
        IJavaScriptResultDescriptorSizeResolver
    {
        public int Resolve(CliTypeIdentity result) => size;
    }

    private sealed class RecordingArgumentEmitter : IJavaScriptImportArgumentEmitter
    {
        public int Count { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            CliTypeIdentity type,
            int local,
            MethodEmissionContext context) => Count++;
    }

    private sealed class RecordingCallbackHandleEmitter : IHostCallbackHandleEmitter
    {
        public int Count { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            int delegateLocal,
            MethodEmissionContext context) => Count++;
    }

    private sealed class RecordingHandleReleaser : IInteropHandleReleaser
    {
        public int Count { get; private set; }

        public void Release(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            InteropMarshallingTarget target) => Count++;

        public void Release(
            IWasmInstructionWriter code,
            MethodEmissionContext context) => Count++;
    }

    private sealed class RecordingResultEmitter : IJavaScriptImportResultEmitter
    {
        public JavaScriptImportResultRequest? Request { get; private set; }

        public void Emit(
            JavaScriptImportResultRequest request,
            IWasmInstructionWriter code) => Request = request;
    }

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind) =>
            Kinds.Add(kind);
    }
}
