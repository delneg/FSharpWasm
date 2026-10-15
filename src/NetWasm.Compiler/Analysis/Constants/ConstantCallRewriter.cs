using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ConstantCallRewriter(
    IControlFlowGraphBuilder graphs,
    ICalledMethodResolver calls,
    IConstantCallEvaluator constants) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ICalledMethodResolver _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    private readonly IConstantCallEvaluator _constants = constants ?? throw new ArgumentNullException(nameof(constants));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var graph = _graphs.Build(body);
        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in graph.Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 0; index < operations.Length; index++)
            {
                var call = operations[index];
                if (call.Operation != CilOperation.Call || _calls.Resolve(call) is not { Definition.IsStatic: true } target)
                {
                    continue;
                }
                var count = target.Signature.ParameterTypes.Length;
                if (index < count) continue;
                var arguments = operations[(index - count)..index].Select(instruction => instruction switch
                {
                    { Operation: CilOperation.LoadNull } => (ScalarConstant)new ScalarConstant.Null(),
                    { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 value } => new ScalarConstant.Int32(value.Value),
                    _ => null,
                }).ToArray();
                if (arguments.Any(value => value is null) || _constants.Evaluate(new(target, [.. arguments])) is not { } result)
                {
                    continue;
                }
                for (var argument = index - count; argument < index; argument++)
                {
                    var instruction = operations[argument];
                    rewritten[instruction.Offset] = instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                    operations[argument] = rewritten[instruction.Offset];
                }
                var replacement = result is ScalarConstant.Int32 integer
                    ? call with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(integer.Value) }
                    : call with { Operation = CilOperation.LoadNull, Operand = new CilOperand.None() };
                rewritten[call.Offset] = replacement;
                operations[index] = replacement;
                changed = true;
            }
        }
        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }
}
