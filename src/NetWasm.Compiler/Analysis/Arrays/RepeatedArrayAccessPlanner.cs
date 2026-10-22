using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal interface IRepeatedArrayAccessPlanner
{
    RepeatedArrayAccessPlan Plan(ValidatedControlFlowGraph controlFlow);
}

// Checked: the same array and index were accessed earlier in the block, so
// both checks passed. NonNull: an earlier access to the same array passed its
// null check.
internal sealed record RepeatedArrayAccessPlan(ImmutableHashSet<int> Checked, ImmutableHashSet<int> NonNull)
{
    public static RepeatedArrayAccessPlan Empty { get; } = new([], []);
}

// An access whose array and index an earlier access already checked cannot
// fail its checks again: without exception handlers, a failed check leaves the
// method. Within a block, an access repeats an earlier one while neither its
// array nor its index slot has been stored. Across blocks, an array slot that
// is never stored or exposed keeps the reference an earlier access checked for
// null, so an access in a block dominated by one holding such an access skips
// its null check.
internal sealed class RepeatedArrayAccessPlanner(IControlFlowDominanceAnalyzer dominance) : IRepeatedArrayAccessPlanner
{
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ??
        throw new ArgumentNullException(nameof(dominance));

    public RepeatedArrayAccessPlan Plan(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var graph = controlFlow.Graph;
        if (!graph.MethodBody.ExceptionRegions.IsEmpty) return RepeatedArrayAccessPlan.Empty;
        var exposed = ExposedSlots(graph.MethodBody);
        var stable = StableArraySlots(graph.MethodBody, exposed);
        var checkedAccesses = ImmutableHashSet.CreateBuilder<int>();
        var nonNull = ImmutableHashSet.CreateBuilder<int>();
        var unproven = new List<(int Block, int Offset, Slot Array)>();
        var accessedBlocks = new Dictionary<Slot, HashSet<int>>();
        foreach (var block in graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index)))
        {
            var origins = new List<Origin>();
            var checkedArrays = new HashSet<Slot>();
            var checkedPairs = new HashSet<(Slot Array, Origin Index)>();
            foreach (var instruction in block.Instructions)
            {
                if (!controlFlow.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack))
                {
                    origins.Clear();
                    continue;
                }
                if (origins.Count != stack.Length)
                {
                    origins.Clear();
                    origins.AddRange(Enumerable.Repeat(Origin.Unknown, stack.Length));
                }
                var consumed = instruction.Operation switch
                {
                    CilOperation.LoadArrayElement or CilOperation.LoadArrayElementReference or
                        CilOperation.LoadArrayElementAddress => 2,
                    CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference => 3,
                    _ => 0,
                };
                // A slot exposed by address can change without a store instruction.
                if (consumed != 0 && origins[^consumed].Slot is { } array && !exposed.Contains(array) &&
                    origins[^(consumed - 1)] is var index && !(index.Slot is { } indexSlot && exposed.Contains(indexSlot)) &&
                    stack[stack.Length - consumed + 1] == CliValueKind.I4)
                {
                    if (index.Known && checkedPairs.Contains((array, index)))
                    {
                        checkedAccesses.Add(instruction.Offset);
                    }
                    else if (checkedArrays.Contains(array))
                    {
                        nonNull.Add(instruction.Offset);
                    }
                    else if (stable.Contains(array))
                    {
                        unproven.Add((block.Index, instruction.Offset, array));
                    }
                    checkedArrays.Add(array);
                    if (index.Known) checkedPairs.Add((array, index));
                    if (stable.Contains(array))
                    {
                        if (!accessedBlocks.TryGetValue(array, out var blocks)) accessedBlocks[array] = blocks = [];
                        blocks.Add(block.Index);
                    }
                }
                if (Stored(instruction) is { } stored)
                {
                    checkedArrays.Remove(stored);
                    checkedPairs.RemoveWhere(pair => pair.Array == stored || pair.Index.Slot == stored);
                }
                Track(instruction, origins);
            }
        }
        if (unproven.Count != 0)
        {
            var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
            foreach (var (block, offset, array) in unproven)
            {
                if (accessedBlocks[array].Any(other => other != block && dominators[block].Contains(other)))
                {
                    nonNull.Add(offset);
                }
            }
        }
        return new(checkedAccesses.ToImmutable(), nonNull.ToImmutable());
    }

    private static HashSet<Slot> ExposedSlots(CilMethodBody body) =>
    [
        .. body.Instructions
            .Where(static instruction => instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress)
            .Select(static instruction => new Slot(instruction.Operation == CilOperation.LoadArgumentAddress,
                ((CilOperand.Index)instruction.Operand).Value)),
    ];

    // Loaded array slots that hold one reference for the whole method: never
    // stored after entry and never exposed by address.
    private static HashSet<Slot> StableArraySlots(CilMethodBody body, HashSet<Slot> exposed)
    {
        var slots = body.Instructions.Select(Loaded).OfType<Slot>()
            .Where(slot => !exposed.Contains(slot) && TypeOf(body, slot)?.Shape == CliTypeShape.SzArray)
            .ToHashSet();
        foreach (var instruction in body.Instructions)
        {
            if (Stored(instruction) is { } stored) slots.Remove(stored);
        }
        return slots;
    }

    // An instance method's receiver is its declaring type, never an array.
    private static CliTypeIdentity? TypeOf(CilMethodBody body, Slot slot)
    {
        if (!slot.IsArgument) return body.LocalSignatureTypes[slot.Index];
        var first = body.Method.IsStatic ? 0 : 1;
        return slot.Index < first ? null : body.Method.Signature.ParameterSignatureTypes[slot.Index - first];
    }

    private static Slot? Loaded(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index local } => new(false, local.Value),
        { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index argument } => new(true, argument.Value),
        _ => null,
    };

    private static Slot? Stored(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index local } => new(false, local.Value),
        { Operation: CilOperation.StoreArgument, Operand: CilOperand.Index argument } => new(true, argument.Value),
        _ => null,
    };

    private readonly record struct Slot(bool IsArgument, int Index);

    // A local or argument plus a constant offset, or a constant.
    private readonly record struct Origin(Slot? Slot, int Offset, bool Constant)
    {
        public static Origin Unknown => default;
        public bool Known => Slot is not null || Constant;
    }

    private static void Track(CilInstruction instruction, List<Origin> origins)
    {
        switch (instruction.Operation)
        {
            case CilOperation.LoadLocal or CilOperation.LoadArgument:
                origins.Add(new(Loaded(instruction), 0, false));
                return;
            case CilOperation.StoreLocal or CilOperation.StoreArgument:
                Consume(origins, 1, false);
                var stored = Stored(instruction);
                // A value already on the stack still holds the old one.
                for (var i = 0; i < origins.Count; i++)
                    if (origins[i].Slot == stored) origins[i] = Origin.Unknown;
                return;
            case CilOperation.LoadInt32 when instruction.Operand is CilOperand.ConstantI4 constant:
                origins.Add(new(null, constant.Value, true));
                return;
            case CilOperation.Add or CilOperation.Subtract when origins.Count >= 2 &&
                origins[^2] is { Slot: not null } left && origins[^1] is { Constant: true } right:
                origins.RemoveRange(origins.Count - 2, 2);
                var sum = instruction.Operation == CilOperation.Add
                    ? (long)left.Offset + right.Offset
                    : (long)left.Offset - right.Offset;
                origins.Add(sum is >= int.MinValue and <= int.MaxValue ? left with { Offset = (int)sum } : Origin.Unknown);
                return;
            case CilOperation.Duplicate:
                origins.Add(origins[^1]);
                return;
            case CilOperation.Nop or CilOperation.Readonly or CilOperation.Volatile or CilOperation.Unaligned:
                return;
            case CilOperation.Negate or CilOperation.OnesComplement:
                Consume(origins, 1, true);
                return;
            case CilOperation.LoadArrayElement or CilOperation.LoadArrayElementReference or
                CilOperation.LoadArrayElementAddress:
            case >= CilOperation.Add and <= CilOperation.RemainderUnsigned:
            case >= CilOperation.CompareEqual and <= CilOperation.CompareLessThanUnsigned:
                Consume(origins, 2, true);
                return;
            case CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference:
                Consume(origins, 3, false);
                return;
            case CilOperation.LoadArrayLength or CilOperation.LoadField or CilOperation.LoadFieldAddress or
                CilOperation.LoadObject:
            case >= CilOperation.ConvertInt32 and <= CilOperation.ConvertNumeric:
                Consume(origins, 1, true);
                return;
            case CilOperation.Pop or CilOperation.StoreStaticField:
                Consume(origins, 1, false);
                return;
            case CilOperation.StoreField or CilOperation.StoreObject:
                Consume(origins, 2, false);
                return;
            case >= CilOperation.LoadInt32 and <= CilOperation.LoadFieldToken:
            case CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress or CilOperation.LoadStaticField or
                CilOperation.LoadStaticFieldAddress or CilOperation.SizeOf:
                origins.Add(Origin.Unknown);
                return;
            default:
                // A call or branch ends what the tracked stack can say.
                origins.Clear();
                return;
        }
    }

    private static void Consume(List<Origin> origins, int count, bool produces)
    {
        origins.RemoveRange(origins.Count - count, count);
        if (produces) origins.Add(Origin.Unknown);
    }
}
