using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ConstantBranchRewriter(IControlFlowGraphBuilder graphs) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var graph = _graphs.Build(body);
        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in graph.Blocks)
        {
            var locals = new Dictionary<int, int>();
            var producers = new List<CilInstruction>();
            foreach (var original in block.Instructions)
            {
                var instruction = original;
                if (instruction.Operation == CilOperation.Nop) continue;
                if (instruction is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index local } &&
                    locals.TryGetValue(local.Value, out var constant))
                {
                    instruction = instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(constant) };
                    rewritten[instruction.Offset] = instruction;
                    changed = true;
                }

                if (instruction is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 })
                {
                    producers.Add(instruction);
                    continue;
                }

                if (instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index stored })
                {
                    if (producers.Count > 0)
                    {
                        locals[stored.Value] = ((CilOperand.ConstantI4)producers[^1].Operand).Value;
                        producers.RemoveAt(producers.Count - 1);
                    }
                    else
                    {
                        locals.Remove(stored.Value);
                    }
                    continue;
                }

                if (instruction.Operation == CilOperation.CompareEqual && producers.Count >= 2)
                {
                    var right = producers[^1];
                    var left = producers[^2];
                    rewritten[left.Offset] = left with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                    rewritten[right.Offset] = right with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                    var equal = ((CilOperand.ConstantI4)left.Operand).Value == ((CilOperand.ConstantI4)right.Operand).Value;
                    instruction = instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(equal ? 1 : 0) };
                    rewritten[instruction.Offset] = instruction;
                    producers.RemoveRange(producers.Count - 2, 2);
                    producers.Add(instruction);
                    changed = true;
                    continue;
                }

                if (instruction.Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse && producers.Count > 0)
                {
                    var condition = producers[^1];
                    var value = ((CilOperand.ConstantI4)condition.Operand).Value != 0;
                    var taken = value == (instruction.Operation == CilOperation.BranchIfTrue);
                    rewritten[condition.Offset] = condition with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                    rewritten[instruction.Offset] = instruction with
                    {
                        Operation = taken ? CilOperation.Branch : CilOperation.Nop,
                        Operand = taken ? instruction.Operand : new CilOperand.None(),
                    };
                    changed = true;
                }

                // No inference through effects, aliases or unknown stack transformations.
                producers.Clear();
                locals.Clear();
            }
        }

        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }
}
