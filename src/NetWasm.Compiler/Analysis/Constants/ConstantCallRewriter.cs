using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ConstantCallRewriter(
    IControlFlowGraphBuilder graphs,
    ICalledMethodResolver calls,
    IConstantCallEvaluator constants,
    IConstantOperandReader operands) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ICalledMethodResolver _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    private readonly IConstantCallEvaluator _constants = constants ?? throw new ArgumentNullException(nameof(constants));
    private readonly IConstantOperandReader _operands = operands ?? throw new ArgumentNullException(nameof(operands));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(instruction => instruction.Operation is CilOperation.Call or CilOperation.CallVirtual)) return body;
        var graph = _graphs.Build(body);
        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in graph.Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 0; index < operations.Length; index++)
            {
                var call = operations[index];
                if (call.Operation is not (CilOperation.Call or CilOperation.CallVirtual) || _calls.Resolve(call) is not { } target ||
                    call.Operation == CilOperation.CallVirtual && target.Definition.IsStatic)
                {
                    continue;
                }
                var count = target.Signature.ParameterTypes.Length + (target.Definition.IsStatic ? 0 : 1);
                var arguments = new ScalarConstant?[count];
                var start = index;
                for (var argument = count - 1; argument >= 0 && start >= 0; argument--)
                {
                    arguments[argument] = ReadBackwards(operations, ref start);
                    if (arguments[argument] is null) start = -1;
                }
                if (start < 0 || _constants.Evaluate(new(target, [.. arguments])) is not { } result ||
                    Replace(call, result) is not { } replacement)
                {
                    continue;
                }
                for (var argument = start; argument < index; argument++)
                {
                    var instruction = operations[argument];
                    rewritten[instruction.Offset] = instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                    operations[argument] = rewritten[instruction.Offset];
                }
                rewritten[call.Offset] = replacement;
                operations[index] = replacement;
                changed = true;
            }
        }
        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }

    // Reads the operand whose producers end just before `end` and moves `end` to their start.
    private ScalarConstant? ReadBackwards(CilInstruction[] operations, ref int end)
    {
        for (var length = 1; length <= _operands.MaximumLength && length <= end; length++)
        {
            if (_operands.Read(new ArraySegment<CilInstruction>(operations, end - length, length)) is { } value)
            {
                end -= length;
                return value;
            }
        }
        return null;
    }

    private static CilInstruction? Replace(CilInstruction call, ScalarConstant result) => result switch
    {
        ScalarConstant.Int32 integer => call with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(integer.Value) },
        ScalarConstant.Null => call with { Operation = CilOperation.LoadNull, Operand = new CilOperand.None() },
        ScalarConstant.String text => call with { Operation = CilOperation.LoadString, Operand = new CilOperand.UserString(text.Value) },
        _ => null,
    };
}
