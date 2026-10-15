using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed class ArrayLoopUnroller(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IArrayLoopUnrollingPlanner planner,
    bool enabled) : IArrayLoopUnroller
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IArrayLoopUnrollingPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        var proof = body.ArrayBoundsProof;
        if (proof is null || proof.Instructions != body.Instructions || proof.Locals != body.Locals ||
            proof.LocalSignatureTypes != body.LocalSignatureTypes) return body;
        var plans = _planner.Plan(_stacks.Validate(_graphs.Build(body)));
        if (plans.IsEmpty) return body;

        var code = body.Instructions.ToBuilder();
        var locals = body.Locals.ToBuilder();
        var signatures = body.LocalSignatureTypes.ToBuilder();
        var accesses = proof.AccessOffsets.ToBuilder();
        var indexOffsets = proof.IndexOffsets.ToBuilder();
        var redirects = new Dictionary<int, int>();
        var next = body.Instructions[^1].NextOffset;
        foreach (var plan in plans)
        {
            var entry = next;
            foreach (var branch in plan.EntryBranches) redirects.Add(branch, entry);
            var length = locals.Count;
            locals.Add(CliValueKind.I4);
            signatures.Add(CliTypeIdentity.FromStackKind(CliValueKind.I4));
            // The original first guard evaluates length even on a zero-trip
            // path. Keep that throwing evaluation before the fast loop, then
            // reuse its immutable bound without repeating the null check.
            Append(CilOperation.LoadLocal, new CilOperand.Index(plan.ArrayLocal));
            Append(CilOperation.LoadArrayLength, new CilOperand.None());
            Append(CilOperation.ConvertInt32, new CilOperand.None());
            Append(CilOperation.StoreLocal, new CilOperand.Index(length));
            var fastGuard = next;
            Append(CilOperation.LoadLocal, new CilOperand.Index(plan.IndexLocal));
            Append(CilOperation.LoadLocal, new CilOperand.Index(length));
            Append(CilOperation.LoadInt32, new CilOperand.ConstantI4(plan.Factor - 1));
            Append(CilOperation.Subtract, new CilOperand.None());
            Append(CilOperation.BranchIfGreaterThanOrEqualSigned, new CilOperand.BranchTarget(plan.GuardOffset));
            for (var iteration = 0; iteration < plan.Factor; iteration++)
            {
                // The induction proof identifies the final four instructions
                // as index = index + 1. Keep one base for the group instead.
                foreach (var instruction in plan.Body[..^4])
                {
                    if (proof.AccessOffsets.Contains(instruction.Offset))
                    {
                        accesses.Add(next);
                        indexOffsets.Add(next, new(plan.IndexLocal, iteration));
                    }
                    code.Add(instruction with { Offset = next, NextOffset = checked(next + 1), OriginalOffset = instruction.SourceOffset });
                    next++;
                    if (iteration != 0 && instruction.Operation == CilOperation.LoadLocal &&
                        instruction.Operand is CilOperand.Index index && index.Value == plan.IndexLocal)
                    {
                        Append(CilOperation.LoadInt32, new CilOperand.ConstantI4(iteration));
                        Append(CilOperation.Add, new CilOperand.None());
                    }
                }
            }
            Append(CilOperation.LoadLocal, new CilOperand.Index(plan.IndexLocal));
            Append(CilOperation.LoadInt32, new CilOperand.ConstantI4(plan.Factor));
            Append(CilOperation.Add, new CilOperand.None());
            Append(CilOperation.StoreLocal, new CilOperand.Index(plan.IndexLocal));
            Append(CilOperation.Branch, new CilOperand.BranchTarget(fastGuard));

            void Append(CilOperation operation, CilOperand operand)
            {
                code.Add(new(next, checked(next + 1), operation, operand) { OriginalOffset = plan.GuardOffset });
                next++;
            }
        }
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            if (redirects.TryGetValue(code[index].Offset, out var target))
                code[index] = code[index] with { Operand = new CilOperand.BranchTarget(target) };
        }
        var result = body with
        {
            Instructions = code.ToImmutable(),
            MaxStack = Math.Max(3, checked(body.MaxStack + 1)),
            Locals = locals.ToImmutable(),
            LocalSignatureTypes = signatures.ToImmutable(),
            ArrayBoundsProof = null,
        };
        _stacks.Validate(_graphs.Build(result));
        return result with
        {
            ArrayBoundsProof = new(result.Instructions, result.Locals, result.LocalSignatureTypes, accesses.ToImmutable())
            {
                IndexOffsets = indexOffsets.ToImmutable(),
            },
        };
    }
}
