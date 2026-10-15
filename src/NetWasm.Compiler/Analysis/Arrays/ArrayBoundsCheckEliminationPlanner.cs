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
        var loops = _induction.Analyze(controlFlow);
        if (loops.IsEmpty) return [];
        var graph = controlFlow.Graph;
        var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        var accesses = ImmutableHashSet.CreateBuilder<int>();
        foreach (var loop in loops)
        {
            foreach (var blockIndex in loop.LoopBlocks)
            {
                if (blockIndex == loop.GuardBlock ||
                    !dominators[blockIndex].Contains(loop.BodyEntryBlock)) continue;
                var origins = new List<int?>();
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
                        origins.AddRange(Enumerable.Repeat<int?>(null, stack.Length));
                    }
                    var consumed = instruction.Operation switch
                    {
                        CilOperation.LoadArrayElement or CilOperation.LoadArrayElementReference or
                            CilOperation.LoadArrayElementAddress => 2,
                        CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference => 3,
                        _ => 0,
                    };
                    if (consumed != 0 && origins.Count >= consumed &&
                        origins[^consumed] == loop.ArrayLocal &&
                        origins[^(consumed - 1)] == loop.IndexLocal &&
                        stack[stack.Length - consumed + 1] == CliValueKind.I4)
                        accesses.Add(instruction.Offset);
                    Track(instruction, origins);
                }
            }
        }
        return accesses.ToImmutable();
    }

    private static void Track(CilInstruction instruction, List<int?> origins)
    {
        switch (instruction.Operation)
        {
            case CilOperation.LoadLocal:
                origins.Add(((CilOperand.Index)instruction.Operand).Value);
                return;
            case CilOperation.StoreLocal:
                Consume(origins, 1, false);
                var local = ((CilOperand.Index)instruction.Operand).Value;
                for (var i = 0; i < origins.Count; i++)
                    if (origins[i] == local) origins[i] = null;
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
                origins.Add(null);
                return;
            default:
                // No cross-call or cross-block expression provenance is inferred.
                origins.Clear();
                return;
        }
    }

    private static void Consume(List<int?> origins, int count, bool produces)
    {
        origins.RemoveRange(origins.Count - count, count);
        if (produces) origins.Add(null);
    }
}
