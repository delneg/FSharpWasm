using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Devirtualization;

// A local that only ever holds the object one allocation created has that
// allocation's exact class, so a virtual or interface call on it reaches one
// implementation, the way .NET's JIT devirtualizes from an allocation. The call
// becomes direct, and since an allocation is never null it needs no null check,
// so the O3 inliner can take the implementation too.
internal sealed class ExactReceiverDevirtualizer(
    ICalledMethodResolver calledMethods,
    IDispatchTargetResolver dispatchTargets,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IControlFlowDominanceAnalyzer dominance,
    bool enabled) : IExactReceiverDevirtualizer
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IDispatchTargetResolver _dispatchTargets = dispatchTargets ?? throw new ArgumentNullException(nameof(dispatchTargets));
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ?? throw new ArgumentNullException(nameof(dominance));
    // The dispatch resolver caches without locking; methods are optimized in parallel.
    private readonly object _dispatchLock = new();

    public CilMethodBody Rewrite(
        CilMethodBody body,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> knownStacks)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(knownStacks);
        var instructions = body.Instructions;
        if (!enabled ||
            !instructions.Any(static instruction => instruction.Operation == CilOperation.CallVirtual) ||
            !instructions.Any(static instruction => instruction.Operation == CilOperation.NewObject))
        {
            return body;
        }

        var graph = _graphs.Build(body);
        var entryStacks = knownStacks.Count != 0 ? knownStacks : _stacks.Validate(graph).InstructionEntryStacks;
        var producers = new StackSlotProducers(body, entryStacks);
        var allocations = ExactLocals(body, producers);
        if (allocations.Count == 0)
        {
            return body;
        }

        var blocks = new Dictionary<int, int>();
        foreach (var block in graph.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                blocks[instruction.Offset] = block.Index;
            }
        }
        var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        var rewritten = instructions.ToBuilder();
        var changed = false;
        for (var index = 1; index < instructions.Length; index++)
        {
            var call = instructions[index];
            var constrained = instructions[index - 1].Operation == CilOperation.Constrained;
            if (call.Operation != CilOperation.CallVirtual ||
                _calledMethods.Resolve(call) is not { Definition.IsStatic: false } declaration ||
                (constrained
                    ? index < 2 || !IsConstrainedRead(instructions, producers, index - 2) ? null : index - 2
                    : entryStacks.TryGetValue(call.Offset, out var stack)
                        ? producers.Find(index, stack.Length - declaration.Signature.ParameterTypes.Length - 1)
                        : null) is not { } receiver ||
                instructions[receiver] is not { Operand: CilOperand.Index local } receiving ||
                receiving.Operation != (constrained ? CilOperation.LoadLocalAddress : CilOperation.LoadLocal) ||
                !allocations.TryGetValue(local.Value, out var allocation) ||
                !Dominates(instructions, blocks, dominators, allocation.Store, receiver))
            {
                continue;
            }
            DispatchTargetModel? target;
            lock (_dispatchLock)
            {
                target = _dispatchTargets.Resolve(declaration, allocation.Type);
            }
            if (target?.Method is not { Definition.HasManagedBody: true } implementation)
            {
                continue;
            }
            if (constrained)
            {
                // A constrained call on a reference type loads the reference from the address.
                rewritten[receiver] = instructions[receiver] with { Operation = CilOperation.LoadLocal };
                rewritten[index - 1] = instructions[index - 1] with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            }
            rewritten[index] = call with
            {
                Operation = CilOperation.Call,
                Operand = new CilOperand.MethodInstance(implementation),
            };
            changed = true;
        }
        return changed ? body with { Instructions = rewritten.ToImmutable() } : body;
    }

    // Locals stored once, from an allocation of a class, and never exposed by
    // address except as the receiver of a constrained call.
    private Dictionary<int, (int Store, CliTypeIdentity Type)> ExactLocals(
        CilMethodBody body,
        StackSlotProducers producers)
    {
        var stores = new Dictionary<int, List<int>>();
        var exposed = new HashSet<int>();
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            switch (body.Instructions[index])
            {
                case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index stored }:
                    if (!stores.TryGetValue(stored.Value, out var list)) stores[stored.Value] = list = [];
                    list.Add(index);
                    break;
                case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index address }
                    when !IsConstrainedRead(body.Instructions, producers, index):
                    exposed.Add(address.Value);
                    break;
            }
        }
        var exact = new Dictionary<int, (int, CliTypeIdentity)>();
        foreach (var (local, list) in stores)
        {
            if (list is [var store] && !exposed.Contains(local) &&
                Allocation(body, producers, store) is { } type)
            {
                exact[local] = (store, type);
            }
        }
        return exact;
    }

    // A local's address that only serves as the receiver of a constrained call
    // on a reference type, which dereferences it; the call takes no arguments,
    // so the address is the receiver and not a by-reference argument.
    private bool IsConstrainedRead(ImmutableArray<CilInstruction> instructions, StackSlotProducers producers, int address) =>
        address + 2 < instructions.Length &&
        instructions[address].Operation == CilOperation.LoadLocalAddress &&
        instructions[address + 1] is { Operation: CilOperation.Constrained, Operand: CilOperand.TypeIdentity { Value.IsValueType: false } } &&
        instructions[address + 2].Operation == CilOperation.CallVirtual &&
        _calledMethods.Resolve(instructions[address + 2]) is { Definition.IsStatic: false, Signature.ParameterTypes.IsEmpty: true } &&
        !producers.Enters(address + 1) && !producers.Enters(address + 2);

    // The class a stored value was allocated as, following copies made by dup.
    private static CliTypeIdentity? Allocation(
        CilMethodBody body,
        StackSlotProducers producers,
        int consumer)
    {
        while (producers.FindTop(consumer) is { } producer)
        {
            switch (body.Instructions[producer])
            {
                case { Operation: CilOperation.Duplicate }:
                    consumer = producer;
                    continue;
                case { Operation: CilOperation.NewObject, Operand: CilOperand.MethodInstance { Value.DeclaringType: var type } }
                    when !type.IsValueType && type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation:
                    return type;
                default:
                    return null;
            }
        }
        return null;
    }

    // The store runs before the load on every path that reaches the load.
    private static bool Dominates(
        ImmutableArray<CilInstruction> instructions,
        Dictionary<int, int> blocks,
        Dictionary<int, ImmutableHashSet<int>> dominators,
        int store,
        int load)
    {
        var storeBlock = blocks[instructions[store].Offset];
        var loadBlock = blocks[instructions[load].Offset];
        // A load with stack facts is reachable, so its block has dominators.
        return storeBlock == loadBlock ? store < load : dominators[loadBlock].Contains(storeBlock);
    }
}
