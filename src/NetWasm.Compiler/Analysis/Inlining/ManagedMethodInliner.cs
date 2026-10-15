using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal sealed class ManagedMethodInliner(
    IManagedInliningResolver resolver,
    ITerminalContinuationInliningPlanner terminalContinuations) :
    IManagedMethodInliner
{
    private readonly IManagedInliningResolver _resolver = resolver ??
        throw new ArgumentNullException(nameof(resolver));
    private readonly ITerminalContinuationInliningPlanner _terminalContinuations =
        terminalContinuations ??
        throw new ArgumentNullException(nameof(terminalContinuations));

    public CilMethodBody Inline(
        MethodInstanceModel caller,
        CilMethodBody body,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> instructionEntryStacks)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(instructionEntryStacks);
        if (!body.ExceptionRegions.IsEmpty)
        {
            return body;
        }

        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var expanded = new List<PendingInstruction>(body.Instructions.Length);
        var offsets = new Dictionary<int, int>();
        var maximumInlineStack = 0;
        var changed = false;

        for (var callerIndex = 0;
             callerIndex < body.Instructions.Length;
             callerIndex++)
        {
            var instruction = body.Instructions[callerIndex];
            offsets.Add(instruction.Offset, expanded.Count);
            var plan = _resolver.Resolve(new(caller, instruction));
            if (plan is null)
            {
                expanded.Add(new(instruction, RemapCallerBranch: true));
                continue;
            }

            var terminalContinuation = _terminalContinuations.Plan(new(
                body,
                callerIndex,
                plan,
                instructionEntryStacks));
            if (terminalContinuation is null)
            {
                Inline(
                    instruction,
                    plan,
                    locals,
                    localTypes,
                    expanded);
            }
            else
            {
                InlineTerminalContinuation(
                    instruction,
                    plan,
                    terminalContinuation,
                    locals,
                    localTypes,
                    expanded);
                callerIndex = terminalContinuation.ConsumedThroughIndex;
            }
            maximumInlineStack = Math.Max(maximumInlineStack, plan.Body.MaxStack);
            changed = true;
        }

        if (!changed)
        {
            return body;
        }

        offsets.Add(body.Instructions[^1].NextOffset, expanded.Count);
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>(expanded.Count);
        for (var index = 0; index < expanded.Count; index++)
        {
            var pending = expanded[index];
            instructions.Add(pending.Instruction with
            {
                Offset = index,
                NextOffset = index + 1,
                OriginalOffset = pending.Instruction.SourceOffset,
                Operand = pending.RemapCallerBranch
                    ? Remap(pending.Instruction.Operand, offsets)
                    : pending.Instruction.Operand,
            });
        }

        return body with
        {
            MaxStack = body.MaxStack + maximumInlineStack,
            Locals = locals.ToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
            Instructions = instructions.MoveToImmutable(),
        };
    }

    private static void InlineTerminalContinuation(
        CilInstruction call,
        ManagedInliningPlan plan,
        TerminalContinuationInliningPlan continuation,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        List<PendingInstruction> expanded)
    {
        var (argumentStart, calleeLocalStart) = AppendInlineLocals(
            plan,
            locals,
            localTypes);
        AppendArgumentStores(call, plan.Method, argumentStart, expanded);

        var calleeOffsets = CreateTerminalContinuationCalleeOffsets(
            plan.Body,
            expanded.Count,
            continuation.Suffix.Length);
        foreach (var instruction in plan.Body.Instructions)
        {
            if (instruction.Operation == CilOperation.Return)
            {
                foreach (var suffixInstruction in continuation.Suffix)
                {
                    expanded.Add(new(
                        suffixInstruction,
                        RemapCallerBranch: false));
                }
                continue;
            }

            var rewritten = RewriteLocalAccess(
                instruction,
                call.SourceOffset,
                argumentStart,
                calleeLocalStart);
            expanded.Add(new(
                rewritten with
                {
                    Operand = Remap(rewritten.Operand, calleeOffsets),
                },
                RemapCallerBranch: false));
        }
    }

    private static void Inline(
        CilInstruction call,
        ManagedInliningPlan plan,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        List<PendingInstruction> expanded)
    {
        var (argumentStart, calleeLocalStart) = AppendInlineLocals(
            plan,
            locals,
            localTypes);
        AppendArgumentStores(call, plan.Method, argumentStart, expanded);

        var calleeOffsets = CreateCalleeOffsets(plan.Body, expanded.Count);
        var continuation = calleeOffsets[plan.Body.Instructions[^1].NextOffset];
        foreach (var instruction in plan.Body.Instructions[..^1])
        {
            if (instruction.Operation == CilOperation.Return)
            {
                expanded.Add(new(CreateSynthetic(
                    call,
                    CilOperation.Branch,
                    new CilOperand.BranchTarget(continuation)),
                    RemapCallerBranch: false));
                continue;
            }

            var rewritten = RewriteLocalAccess(
                instruction,
                call.SourceOffset,
                argumentStart,
                calleeLocalStart);
            expanded.Add(new(
                rewritten with
                {
                    Operand = Remap(rewritten.Operand, calleeOffsets),
                },
                RemapCallerBranch: false));
        }
    }

    private static (int ArgumentStart, int CalleeLocalStart) AppendInlineLocals(
        ManagedInliningPlan plan,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes)
    {
        var argumentTypes = GetArgumentTypes(plan.Method);
        var argumentStart = locals.Count;
        foreach (var parameter in argumentTypes)
        {
            locals.Add(parameter.StackKind);
            localTypes.Add(parameter);
        }

        var calleeLocalStart = locals.Count;
        locals.AddRange(plan.Body.Locals);
        localTypes.AddRange(plan.Body.LocalSignatureTypes);
        return (argumentStart, calleeLocalStart);
    }

    private static void AppendArgumentStores(
        CilInstruction call,
        MethodInstanceModel method,
        int argumentStart,
        List<PendingInstruction> expanded)
    {
        var argumentTypes = GetArgumentTypes(method);
        for (var argument = argumentTypes.Length - 1;
             argument >= 0;
             argument--)
        {
            expanded.Add(new(CreateSynthetic(
                call,
                CilOperation.StoreLocal,
                new CilOperand.Index(argumentStart + argument)),
                RemapCallerBranch: false));
        }
    }

    private static ImmutableArray<CliTypeIdentity> GetArgumentTypes(
        MethodInstanceModel method) => method.Definition.IsStatic
        ? method.Signature.ParameterSignatureTypes
        : method.Signature.ParameterSignatureTypes.Insert(
            0,
            method.DeclaringType);

    private static Dictionary<int, int> CreateCalleeOffsets(
        CilMethodBody body,
        int firstOutputIndex)
    {
        var offsets = new Dictionary<int, int>(body.Instructions.Length + 1);
        var outputIndex = firstOutputIndex;
        foreach (var instruction in body.Instructions[..^1])
        {
            offsets.Add(instruction.Offset, outputIndex++);
        }

        var terminal = body.Instructions[^1];
        offsets.Add(terminal.Offset, outputIndex);
        offsets.Add(terminal.NextOffset, outputIndex);
        return offsets;
    }

    private static Dictionary<int, int> CreateTerminalContinuationCalleeOffsets(
        CilMethodBody body,
        int firstOutputIndex,
        int continuationLength)
    {
        var offsets = new Dictionary<int, int>(body.Instructions.Length + 1);
        var outputIndex = firstOutputIndex;
        foreach (var instruction in body.Instructions)
        {
            offsets.Add(instruction.Offset, outputIndex);
            outputIndex += instruction.Operation == CilOperation.Return
                ? continuationLength
                : 1;
        }

        offsets.Add(body.Instructions[^1].NextOffset, outputIndex);
        return offsets;
    }

    private static CilInstruction RewriteLocalAccess(
        CilInstruction instruction,
        int sourceOffset,
        int argumentStart,
        int calleeLocalStart)
    {
        var rewritten = instruction.Operation switch
        {
            CilOperation.LoadArgument => Rewrite(
                instruction,
                CilOperation.LoadLocal,
                argumentStart),
            CilOperation.LoadArgumentAddress => Rewrite(
                instruction,
                CilOperation.LoadLocalAddress,
                argumentStart),
            CilOperation.StoreArgument => Rewrite(
                instruction,
                CilOperation.StoreLocal,
                argumentStart),
            CilOperation.LoadLocal or
            CilOperation.LoadLocalAddress or
            CilOperation.StoreLocal => Rewrite(
                instruction,
                instruction.Operation,
                calleeLocalStart),
            _ => instruction,
        };
        return rewritten with
        {
            Offset = -1,
            NextOffset = -1,
            OriginalOffset = sourceOffset,
        };
    }

    private static CilInstruction Rewrite(
        CilInstruction instruction,
        CilOperation operation,
        int start)
    {
        if (instruction.Operand is not CilOperand.Index index)
        {
            throw new InvalidOperationException(
                "Inline argument and local operations require index operands.");
        }

        return instruction with
        {
            Operation = operation,
            Operand = new CilOperand.Index(start + index.Value),
        };
    }

    private static CilInstruction CreateSynthetic(
        CilInstruction call,
        CilOperation operation,
        CilOperand operand) => new(
            -1,
            -1,
            operation,
            operand)
        {
            OriginalOffset = call.SourceOffset,
        };

    private static CilOperand Remap(
        CilOperand operand,
        IReadOnlyDictionary<int, int> offsets) => operand switch
        {
            CilOperand.BranchTarget target => new CilOperand.BranchTarget(
                ResolveOffset(target.Offset, offsets)),
            CilOperand.SwitchTargets targets => new CilOperand.SwitchTargets(
                [.. targets.Offsets.Select(offset => ResolveOffset(offset, offsets))]),
            _ => operand,
        };

    private static int ResolveOffset(
        int offset,
        IReadOnlyDictionary<int, int> offsets) =>
        offsets.TryGetValue(offset, out var mapped)
            ? mapped
            : throw new InvalidOperationException(
                "Inline branch target does not identify an instruction boundary.");

    private sealed record PendingInstruction(
        CilInstruction Instruction,
        bool RemapCallerBranch);
}
