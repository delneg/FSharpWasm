using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed class ArrayBoundsCheckEliminationPlanner(
    IArrayLoopInductionAnalyzer induction,
    IControlFlowDominanceAnalyzer dominance) : IArrayBoundsCheckEliminationPlanner
{
    private readonly IArrayLoopInductionAnalyzer _induction = induction ??
        throw new ArgumentNullException(nameof(induction));
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ??
        throw new ArgumentNullException(nameof(dominance));

    public ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var accesses = ImmutableHashSet.CreateBuilder<int>();
        PlanLastElements(controlFlow, accesses);
        var loops = _induction.Analyze(controlFlow);
        if (loops.IsEmpty) return accesses.ToImmutable();
        var graph = controlFlow.Graph;
        var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        foreach (var loop in loops)
        {
            foreach (var blockIndex in loop.LoopBlocks)
            {
                if (blockIndex == loop.GuardBlock ||
                    !dominators[blockIndex].Contains(loop.BodyEntryBlock)) continue;
                var origins = new List<Origin>();
                foreach (var instruction in graph.GetBlock(blockIndex).Instructions)
                {
                    if (instruction.Offset == loop.IncrementOffset) break;
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
                    if (consumed != 0 && origins.Count >= consumed &&
                        origins[^consumed] == Origin.FromLocal(loop.ArrayLocal) &&
                        origins[^(consumed - 1)] is { Local: var index, Offset: var offset, Constant: false, Length: false } &&
                        index == loop.IndexLocal && offset >= 0 && offset <= loop.Lookahead &&
                        stack[stack.Length - consumed + 1] == CliValueKind.I4)
                        accesses.Add(instruction.Offset);
                    Track(instruction, origins);
                }
            }
        }
        return accesses.ToImmutable();
    }

    // `if (a.Length > 0) ... a[^1]`: the block entered only when the length is
    // nonzero proves a[a.Length - 1], and its guard's ldlen already checked a
    // for null. The proof lasts until a is stored.
    private static void PlanLastElements(ValidatedControlFlowGraph controlFlow, ImmutableHashSet<int>.Builder accesses)
    {
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        foreach (var guard in graph.Blocks)
        {
            var code = guard.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            if (code is not [.., { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index array },
                { Operation: CilOperation.LoadArrayLength },
                { Operation: CilOperation.BranchIfFalse or CilOperation.BranchIfTrue, Operand: CilOperand.BranchTarget target } branch] ||
                body.LocalSignatureTypes[array.Value].Shape != CliTypeShape.SzArray ||
                body.Instructions.Any(instruction => instruction is
                { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index exposed } && exposed.Value == array.Value))
            {
                continue;
            }
            var nonEmpty = graph.GetBlockAtOffset(branch.Operation == CilOperation.BranchIfTrue
                ? target.Offset
                : branch.NextOffset);
            if (graph.Predecessors[nonEmpty.Index] is not [var only] || only != guard.Index)
            {
                continue;
            }
            var origins = new List<Origin>();
            foreach (var instruction in nonEmpty.Instructions)
            {
                if (!controlFlow.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack) ||
                    instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index stored } && stored.Value == array.Value)
                {
                    break;
                }
                if (origins.Count != stack.Length)
                {
                    origins.Clear();
                    origins.AddRange(Enumerable.Repeat(Origin.Unknown, stack.Length));
                }
                if (instruction.Operation is CilOperation.LoadArrayElement or CilOperation.LoadArrayElementReference or
                        CilOperation.LoadArrayElementAddress && origins.Count >= 2 &&
                    origins[^2] == Origin.FromLocal(array.Value) &&
                    origins[^1] == new Origin(array.Value, -1, false, Length: true) &&
                    stack[^1] == CliValueKind.I4)
                {
                    accesses.Add(instruction.Offset);
                }
                Track(instruction, origins);
            }
        }
    }

    // Where a stack value came from: a local plus a constant offset, the
    // local's array length plus an offset, or a constant.
    private readonly record struct Origin(int? Local, int Offset, bool Constant, bool Length = false)
    {
        public static Origin Unknown => default;
        public static Origin FromLocal(int local) => new(local, 0, false);
    }

    private static void Track(CilInstruction instruction, List<Origin> origins)
    {
        switch (instruction.Operation)
        {
            case CilOperation.LoadLocal:
                origins.Add(Origin.FromLocal(((CilOperand.Index)instruction.Operand).Value));
                return;
            case CilOperation.StoreLocal:
                Consume(origins, 1, false);
                var local = ((CilOperand.Index)instruction.Operand).Value;
                for (var i = 0; i < origins.Count; i++)
                    if (origins[i].Local == local) origins[i] = Origin.Unknown;
                return;
            case CilOperation.LoadInt32 when instruction.Operand is CilOperand.ConstantI4 constant:
                origins.Add(new(null, constant.Value, true));
                return;
            case CilOperation.Add or CilOperation.Subtract when origins.Count >= 2 &&
                origins[^2] is { Local: { } addend, Constant: false } left && origins[^1] is { Constant: true } right:
                origins.RemoveRange(origins.Count - 2, 2);
                var sum = instruction.Operation == CilOperation.Add ? (long)left.Offset + right.Offset : (long)left.Offset - right.Offset;
                origins.Add(sum is >= int.MinValue and <= int.MaxValue
                    ? left with { Offset = (int)sum }
                    : Origin.Unknown);
                return;
            // An array length is at most int.MaxValue, so narrowing it keeps the value.
            case CilOperation.LoadArrayLength when origins.Count >= 1 && origins[^1] is { Local: { } measured, Offset: 0, Constant: false, Length: false }:
                origins[^1] = new(measured, 0, false, Length: true);
                return;
            case CilOperation.ConvertInt32 when origins.Count >= 1 && origins[^1] is { Length: true }:
                return;
            case CilOperation.Duplicate:
                origins.Add(origins[^1]);
                return;
            case CilOperation.Nop:
            case CilOperation.Readonly:
            case CilOperation.Volatile:
            case CilOperation.Unaligned:
                return;
            case CilOperation.Negate:
            case CilOperation.OnesComplement:
                Consume(origins, 1, true);
                return;
            case CilOperation.LoadArrayElement:
            case CilOperation.LoadArrayElementReference:
            case CilOperation.LoadArrayElementAddress:
            case >= CilOperation.Add and <= CilOperation.RemainderUnsigned:
            case >= CilOperation.CompareEqual and <= CilOperation.CompareLessThanUnsigned:
                Consume(origins, 2, true);
                return;
            case CilOperation.StoreArrayElement:
            case CilOperation.StoreArrayElementReference:
                Consume(origins, 3, false);
                return;
            case CilOperation.LoadArrayLength:
            case CilOperation.LoadField:
            case CilOperation.LoadFieldAddress:
            case CilOperation.LoadObject:
            case >= CilOperation.ConvertInt32 and <= CilOperation.ConvertNumeric:
                Consume(origins, 1, true);
                return;
            case CilOperation.Pop:
            case CilOperation.StoreArgument:
            case CilOperation.StoreStaticField:
                Consume(origins, 1, false);
                return;
            case CilOperation.StoreField:
            case CilOperation.StoreObject:
                Consume(origins, 2, false);
                return;
            case >= CilOperation.LoadInt32 and <= CilOperation.LoadFieldToken:
            case CilOperation.LoadArgument:
            case CilOperation.LoadLocalAddress:
            case CilOperation.LoadArgumentAddress:
            case CilOperation.LoadStaticField:
            case CilOperation.LoadStaticFieldAddress:
            case CilOperation.SizeOf:
                origins.Add(Origin.Unknown);
                return;
            default:
                // No cross-call or cross-block expression provenance is inferred.
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
