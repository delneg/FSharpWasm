using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class CreateSpanCallRewriter(
    ICalledMethodResolver calledMethods,
    IReadOnlySpanDataResolver spanData) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions.ToArray();
        var entryOffsets = GetEntryOffsets(body);
        var changed = false;

        for (var index = 1; index < instructions.Length; index++)
        {
            var call = instructions[index];
            var token = instructions[index - 1];
            if (call.Operation != CilOperation.Call ||
                token.Operation != CilOperation.LoadFieldToken ||
                token.NextOffset != call.Offset ||
                entryOffsets.Contains(call.Offset) ||
                token.Operand is not CilOperand.FieldInstance fieldOperand)
            {
                continue;
            }

            var target = calledMethods.Resolve(call);
            var resolved = spanData.Resolve(target, fieldOperand.Value);
            if (resolved is null)
            {
                continue;
            }

            instructions[index - 1] = token with
            {
                Operation = CilOperation.Nop,
                Operand = new CilOperand.None(),
            };
            instructions[index] = call with
            {
                Operation = CilOperation.CreateReadOnlySpanFromRva,
                Operand = resolved,
            };
            changed = true;
        }

        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private static HashSet<int> GetEntryOffsets(CilMethodBody body)
    {
        var offsets = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            switch (instruction.Operand)
            {
                case CilOperand.BranchTarget branch:
                    offsets.Add(branch.Offset);
                    break;
                case CilOperand.SwitchTargets branches:
                    foreach (var offset in branches.Offsets)
                    {
                        offsets.Add(offset);
                    }
                    break;
            }
        }
        foreach (var region in body.ExceptionRegions)
        {
            offsets.Add(region.TryOffset);
            offsets.Add(checked(region.TryOffset + region.TryLength));
            offsets.Add(region.HandlerOffset);
            offsets.Add(checked(region.HandlerOffset + region.HandlerLength));
            if (region.FilterOffset is int filterOffset)
            {
                offsets.Add(filterOffset);
            }
        }
        return offsets;
    }
}
