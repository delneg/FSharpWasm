using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IIntrinsicAddressBodyPlanner
{
    ReachableMethodRequest? Plan(MethodInstanceModel method);
}

// Call sites expand a runtime intrinsic in place, so it has no function of its own. A delegate
// or function pointer to it needs one. A method with managed CIL gets that body, as upstream
// runs it for indirect calls; one without forwards its arguments to itself, and that call site
// expands the intrinsic.
internal sealed class IntrinsicAddressBodyPlanner(IRuntimeIntrinsicRegistry intrinsics) : IIntrinsicAddressBodyPlanner
{
    private readonly IRuntimeIntrinsicRegistry _intrinsics =
        intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public ReachableMethodRequest? Plan(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!_intrinsics.TryGetIntrinsic(method.Definition.Key, out _))
        {
            return null;
        }
        if (method.Definition.HasManagedBody)
        {
            return new(method);
        }

        var arguments = method.Definition.WasmParameterTypes.Length;
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>(arguments + 2);
        for (var index = 0; index < arguments; index++)
        {
            Emit(CilOperation.LoadArgument, new CilOperand.Index(index));
        }
        Emit(CilOperation.Call, new CilOperand.MethodInstance(method));
        Emit(CilOperation.Return, new CilOperand.None());
        return new(method)
        {
            RewrittenBody = new(method.Definition, Math.Max(arguments, 1), [], instructions.ToImmutable())
            {
                MethodInstance = method,
            },
        };

        void Emit(CilOperation operation, CilOperand operand) =>
            instructions.Add(new CilInstruction(instructions.Count, instructions.Count + 1, operation, operand));
    }
}
