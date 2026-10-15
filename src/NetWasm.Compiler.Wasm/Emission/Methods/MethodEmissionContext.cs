using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record MethodEmissionContext(
    MethodRootMap RootMap,
    int LocalBase,
    int StackBase,
    EvaluationStackLocalLayout StackLocals,
    int ObjectTemporary,
    int RootFrame,
    int ExceptionTemporary,
    IReadOnlyDictionary<StructuredExceptionGroupId, int> ExceptionFrameLocals,
    IReadOnlyDictionary<StructuredExceptionGroupId, int> ExceptionContinuationLocals,
    int ValueFrame,
    ValueFrameLayout ValueLayout,
    int FilterRootFrame,
    FilterEnvironmentLayout FilterEnvironment,
    int ParameterOffset,
    int InteropDescriptor,
    int InteropResult,
    int InteropHandle,
    int NumericTemporaryI4,
    int NumericTemporaryI8,
    int DispatcherProgramCounter,
    bool LeaveFrameOnRethrow = true,
    bool LeaveFrameOnExceptionalExit = true,
    StructuredExceptionGroup? ActiveExceptionGroup = null,
    bool IsFilterFunclet = false,
    int StackTraceMethodId = 0,
    RuntimeImportSelection RuntimeImportSelection = default,
    ManagedMethodIdentity CallerIdentity = default)
{
    // Shared exception definitions can have several emitted occurrences. Copies
    // execute normally but must not each claim ownership of definition blocks.
    public HashSet<StructuredExceptionGroupId> EmittedExceptionGroups { get; init; } = [];

    public bool RecordOriginalBlockOwnership { get; init; } = true;

    public ArrayMetadataCachingLayout ArrayMetadata { get; init; } = ArrayMetadataCachingLayout.Empty;

    public TerminalBranchValueFramePlacement? TerminalBranchValueFramePlacement { get; init; }

    public TransientAggregatePromotionLayout TransientAggregatePromotions { get; init; } =
        TransientAggregatePromotionLayout.Empty;

    public AddressTakenScalarPromotionLayout AddressTakenScalarPromotions { get; init; } =
        AddressTakenScalarPromotionLayout.Empty;

    public AddressTakenAggregatePromotionLayout AddressTakenAggregatePromotions
    { get; init; } = AddressTakenAggregatePromotionLayout.Empty;

    public ManagedByRefAggregatePromotionLayout ManagedByRefAggregatePromotions
    { get; init; } = ManagedByRefAggregatePromotionLayout.Empty;

    public ManagedAggregateValueLayout ManagedAggregateValues { get; init; } =
        ManagedAggregateValueLayout.Empty;

    public ImmutableDictionary<int, int> AnchorLocals { get; init; } = [];
    public ImmutableDictionary<RootSource, int> AuthoritativeRootSlots { get; init; } = [];
    public ImmutableDictionary<(int Offset, bool ConstructorCall), ImmutableHashSet<int>> RootClearElisions
    { get; init; } = [];
    public ImmutableDictionary<int, ImmutableHashSet<int>> RootValueElisions { get; init; } = [];

    public IReadOnlyDictionary<StructuredExceptionGroupId, int> ExceptionRootSlots { get; init; } =
        ImmutableDictionary<StructuredExceptionGroupId, int>.Empty;
    public int? ActiveCatchRootSlot { get; init; }
    public int RootSlotCount => checked(RootMap.SlotCount + ExceptionRootSlots.Count);

    public int NumericTemporaryI4Second => checked(DispatcherProgramCounter + 1);
    public int NumericTemporaryI4Third => checked(DispatcherProgramCounter + 2);
    public int NumericTemporaryI4Fourth => checked(DispatcherProgramCounter + 3);
    public int NumericTemporaryI4Fifth => checked(DispatcherProgramCounter + 4);
}
