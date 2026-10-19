using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal static class InstructionEntries
{
    // Offsets that control can reach other than from the instruction before:
    // branch and switch targets and protected region boundaries.
    public static HashSet<int> Of(CilMethodBody body)
    {
        var entries = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            switch (instruction.Operand)
            {
                case CilOperand.BranchTarget target:
                    entries.Add(target.Offset);
                    break;
                case CilOperand.SwitchTargets targets:
                    entries.UnionWith(targets.Offsets);
                    break;
            }
        }
        foreach (var region in body.ExceptionRegions)
        {
            entries.UnionWith([region.TryOffset, region.TryOffset + region.TryLength,
                region.HandlerOffset, region.HandlerOffset + region.HandlerLength]);
            if (region.FilterOffset is { } filter) entries.Add(filter);
        }
        return entries;
    }
}
