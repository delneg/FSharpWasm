using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class CallArgumentConstantCollector(ICalledMethodResolver calls) : ICallArgumentConstantCollector
{
    private readonly ICalledMethodResolver _calls = calls ?? throw new ArgumentNullException(nameof(calls));

    public ImmutableArray<ConstantCallEvaluation> Collect(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var result = ImmutableArray.CreateBuilder<ConstantCallEvaluation>();
        foreach (var block in method.ControlFlow.Graph.Blocks)
        {
            var stack = new List<ScalarConstant?>();
            foreach (var instruction in block.Instructions)
            {
                if (!method.ControlFlow.InstructionEntryStacks.TryGetValue(instruction.Offset, out var entry)) continue;
                if (stack.Count != entry.Length)
                {
                    stack.Clear();
                    for (var index = 0; index < entry.Length; index++) stack.Add(null);
                }
                if (instruction.Operation == CilOperation.Call && _calls.Resolve(instruction) is { Definition.IsStatic: true } target)
                {
                    var count = target.Signature.ParameterTypes.Length;
                    if (count > stack.Count) throw new InvalidOperationException("Validated call stack is shorter than its signature.");
                    result.Add(new(target, [.. stack.GetRange(stack.Count - count, count)]));
                }
                switch (instruction)
                {
                    case { Operation: CilOperation.Nop }:
                        break;
                    case { Operation: CilOperation.LoadNull }:
                        stack.Add(new ScalarConstant.Null());
                        break;
                    case { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 integer }:
                        stack.Add(new ScalarConstant.Int32(integer.Value));
                        break;
                    case
                    {
                        Operation: CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or
                        CilOperation.LoadLocal or CilOperation.LoadLocalAddress or CilOperation.LoadString
                    }:
                        stack.Add(null);
                        break;
                    case { Operation: CilOperation.Duplicate } when stack.Count != 0:
                        stack.Add(stack[^1]);
                        break;
                    case { Operation: CilOperation.Pop } when stack.Count != 0:
                        stack.RemoveAt(stack.Count - 1);
                        break;
                    default:
                        // The validated next entry restores stack height, but no
                        // value facts survive an unmodelled operation or call.
                        stack.Clear();
                        break;
                }
            }
        }
        return result.ToImmutable();
    }
}
