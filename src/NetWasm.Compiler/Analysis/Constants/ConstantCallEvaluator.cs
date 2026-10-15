using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ConstantCallEvaluator(
    IMethodBodyReader methodBodies,
    ICalledMethodResolver calledMethods,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IRuntimeIntrinsicRegistry intrinsics) : IConstantCallEvaluator
{
    private readonly IMethodBodyReader _methodBodies = methodBodies ?? throw new ArgumentNullException(nameof(methodBodies));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public ScalarConstant? Evaluate(ConstantCallEvaluation request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        if (request.Arguments.IsDefault || request.Arguments.Length != request.Method.Signature.ParameterTypes.Length)
        {
            throw new ArgumentException("Constant arguments must match the method signature.", nameof(request));
        }
        var active = new HashSet<string>(StringComparer.Ordinal);
        var first = CreateFrame(request, active);
        if (first is null) return null;
        var frames = new Stack<EvaluationFrame>();
        frames.Push(first);
        while (true)
        {
            var frame = frames.Peek();
            // A repeated instruction is not a finite proof; decline the fold.
            if (!frame.Visited.Add(frame.Offset) || !frame.Instructions.TryGetValue(frame.Offset, out var instruction)) return null;
            frame.Offset = instruction.NextOffset;
            var stack = frame.Values;
            var locals = frame.Locals;
            switch (instruction)
            {
                case { Operation: CilOperation.Nop }:
                    break;
                case { Operation: CilOperation.LoadNull }:
                    stack.Add(new ScalarConstant.Null());
                    break;
                case { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 value }:
                    stack.Add(new ScalarConstant.Int32(value.Value));
                    break;
                case { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index argument }:
                    if ((uint)argument.Value >= (uint)frame.Request.Arguments.Length || frame.Request.Arguments[argument.Value] is not { } known)
                    {
                        return null;
                    }
                    stack.Add(known);
                    break;
                case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index local }:
                    if (!TryPop(stack, out var stored)) return null;
                    locals[local.Value] = stored;
                    break;
                case { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index local }:
                    if (!locals.TryGetValue(local.Value, out var loaded)) return null;
                    stack.Add(loaded);
                    break;
                case { Operation: CilOperation.Duplicate }:
                    if (stack.Count == 0) return null;
                    stack.Add(stack[^1]);
                    break;
                case { Operation: CilOperation.Pop }:
                    if (!TryPop(stack, out _)) return null;
                    break;
                case { Operation: CilOperation.CompareEqual }:
                    if (!TryPop(stack, out var right) || !TryPop(stack, out var left) || left.GetType() != right.GetType()) return null;
                    stack.Add(new ScalarConstant.Int32(left == right ? 1 : 0));
                    break;
                case { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget branch }:
                    frame.Offset = branch.Offset;
                    break;
                case { Operation: CilOperation.BranchIfTrue or CilOperation.BranchIfFalse, Operand: CilOperand.BranchTarget branch }:
                    if (!TryPop(stack, out var condition)) return null;
                    var truth = condition is ScalarConstant.Int32 { Value: not 0 };
                    if (truth == (instruction.Operation == CilOperation.BranchIfTrue)) frame.Offset = branch.Offset;
                    break;
                case { Operation: CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual, Operand: CilOperand.BranchTarget branch }:
                    if (!TryPop(stack, out var comparedRight) || !TryPop(stack, out var comparedLeft) || comparedLeft.GetType() != comparedRight.GetType()) return null;
                    if ((comparedLeft == comparedRight) == (instruction.Operation == CilOperation.BranchIfEqual)) frame.Offset = branch.Offset;
                    break;
                case { Operation: CilOperation.Call }:
                    var target = _calledMethods.Resolve(instruction);
                    if (target is null || !target.Definition.IsStatic || stack.Count < target.Signature.ParameterTypes.Length) return null;
                    var count = target.Signature.ParameterTypes.Length;
                    var arguments = stack.GetRange(stack.Count - count, count);
                    stack.RemoveRange(stack.Count - count, count);
                    var child = CreateFrame(new(target, [.. arguments]), active);
                    if (child is null) return null;
                    frames.Push(child);
                    break;
                case { Operation: CilOperation.Return }:
                    if (stack.Count != 1 || !Matches(stack[0], frame.Request.Method.Signature.ReturnType)) return null;
                    var returned = stack[0];
                    frames.Pop();
                    active.Remove(frame.Request.Method.CanonicalName);
                    if (frames.Count == 0) return returned;
                    frames.Peek().Values.Add(returned);
                    break;
                default:
                    // No field/array access, allocation, address-taking, arithmetic
                    // with possible exceptions, virtual calls or other effects.
                    return null;
            }
        }

    }

    private EvaluationFrame? CreateFrame(ConstantCallEvaluation request, HashSet<string> active)
    {
        var method = request.Method;
        if (!method.Definition.IsStatic || method.Definition.IsVirtual ||
            method.Definition.IsNoInlining || !method.Definition.HasManagedBody ||
            method.Definition.JSImport is not null || method.Definition.WitImport is not null || method.Definition.NativeImport is not null ||
            _intrinsics.TryGetIntrinsic(method.Definition.Key, out _) ||
            method.Signature.ReturnType is not (CliValueKind.I4 or CliValueKind.ManagedReference) ||
            active.Contains(method.CanonicalName))
        {
            return null;
        }
        var owner = _typeDefinitions.ResolveTypeIdentity(method.DeclaringType);
        if (!owner.IsBeforeFieldInit && owner.Methods.Any(key => _methods.GetMethod(key).Name == ".cctor"))
        {
            return null;
        }
        for (var index = 0; index < request.Arguments.Length; index++)
        {
            if (request.Arguments[index] is { } argument && !Matches(argument, method.Signature.ParameterTypes[index])) return null;
        }
        var body = _methodBodies.ReadMethodBody(method);
        if (!body.ExceptionRegions.IsEmpty || body.Instructions.IsEmpty) return null;
        active.Add(method.CanonicalName);
        return new(request, body);
    }

    private sealed class EvaluationFrame(ConstantCallEvaluation request, CilMethodBody body)
    {
        public ConstantCallEvaluation Request { get; } = request;
        public Dictionary<int, CilInstruction> Instructions { get; } = body.Instructions.ToDictionary(instruction => instruction.Offset);
        public HashSet<int> Visited { get; } = [];
        public Dictionary<int, ScalarConstant> Locals { get; } = [];
        public List<ScalarConstant> Values { get; } = [];
        public int Offset { get; set; } = body.Instructions[0].Offset;
    }

    private static bool Matches(ScalarConstant value, CliValueKind kind) => value switch
    {
        ScalarConstant.Int32 => kind == CliValueKind.I4,
        ScalarConstant.Null => kind == CliValueKind.ManagedReference,
        _ => false,
    };

    private static bool TryPop(List<ScalarConstant> stack, out ScalarConstant value)
    {
        if (stack.Count == 0)
        {
            value = null!;
            return false;
        }
        value = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return true;
    }
}
