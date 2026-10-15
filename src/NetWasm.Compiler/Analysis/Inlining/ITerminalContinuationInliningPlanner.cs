using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal interface ITerminalContinuationInliningPlanner
{
    TerminalContinuationInliningPlan? Plan(
        TerminalContinuationInliningRequest request);
}

internal sealed record TerminalContinuationInliningRequest(
    CilMethodBody CallerBody,
    int CallIndex,
    ManagedInliningPlan Inlining,
    IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> InstructionEntryStacks);

internal sealed record TerminalContinuationInliningPlan(
    ImmutableArray<CilInstruction> Suffix,
    int ConsumedThroughIndex);
