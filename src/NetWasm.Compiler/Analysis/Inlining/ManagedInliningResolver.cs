using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Inlining;

internal sealed class ManagedInliningResolver(
    IMethodBodyReader methodBodies,
    IMethodSpecializer specializer,
    ICalledMethodResolver calledMethods,
    IManagedInliningBodyValidator bodyValidator,
    ManagedMethodInliningPolicy policy,
    IMethodInitializationTriggerClassifier initializationTriggers) : IManagedInliningResolver
{
    private readonly IMethodBodyReader _methodBodies = methodBodies ??
        throw new ArgumentNullException(nameof(methodBodies));
    private readonly IMethodSpecializer _specializer = specializer ??
        throw new ArgumentNullException(nameof(specializer));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));
    private readonly IManagedInliningBodyValidator _bodyValidator = bodyValidator ??
        throw new ArgumentNullException(nameof(bodyValidator));
    private readonly IMethodInitializationTriggerClassifier _initializationTriggers = initializationTriggers ??
        throw new ArgumentNullException(nameof(initializationTriggers));
    private readonly ManagedMethodInliningPolicy _policy = policy switch
    {
        ManagedMethodInliningPolicy.Disabled or
        ManagedMethodInliningPolicy.Conservative => policy,
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    public ManagedInliningPlan? Resolve(ManagedInliningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_policy == ManagedMethodInliningPolicy.Disabled ||
            request.Call.Operation is not (CilOperation.Call or CilOperation.CallVirtual) ||
            _calledMethods.Resolve(request.Call) is not { } method ||
            !method.Definition.HasManagedBody ||
            method.Definition.IsNoInlining ||
            _initializationTriggers.Classify(method.Definition) ||
            method.IsConstructed ||
            method.CanonicalName == request.Caller.CanonicalName ||
            !HasSupportedCallMetadata(request.Call, method))
        {
            return null;
        }

        var body = _specializer.Rewrite(_methodBodies.ReadMethodBody(method)) with
        {
            MethodInstance = method,
        };
        if (!IsSupportedBody(method, body) ||
            !(method.Definition.IsStatic
                ? IsPriorityCandidate(method, body)
                : IsReferenceFieldAccessor(body)))
        {
            return null;
        }

        return new(method, body)
        {
            InstructionEntryStacks = _bodyValidator.Validate(body),
        };
    }

    private static bool HasSupportedCallMetadata(
        CilInstruction call,
        MethodInstanceModel method)
    {
        if (method.Definition.IsStatic)
        {
            return call.Operation == CilOperation.Call &&
                IsPotentialPriorityCandidate(method);
        }

        return !method.DeclaringType.IsValueType &&
            (call.Operation == CilOperation.Call ||
                !method.Definition.IsVirtual ||
                method.Definition.IsFinal);
    }

    private static bool IsPriorityCandidate(
        MethodInstanceModel method,
        CilMethodBody body) =>
        IsExplicitPriorityCandidate(method) ||
        IsAggregateAddressForwardingWrapper(method, body);

    private static bool IsPotentialPriorityCandidate(MethodInstanceModel method) =>
        IsExplicitPriorityCandidate(method) ||
        method.Signature.ReturnSignatureType.StackKind == CliValueKind.ValueType &&
        method.Signature.ParameterSignatureTypes.Any(
            static type => type.StackKind == CliValueKind.ValueType);

    private static bool IsExplicitPriorityCandidate(MethodInstanceModel method) =>
        method.Definition.IsAggressiveInlining ||
        method.Signature.ParameterSignatureTypes.Any(
            static type => type.Shape == CliTypeShape.ManagedByReference);

    private static bool IsAggregateAddressForwardingWrapper(
        MethodInstanceModel method,
        CilMethodBody body)
    {
        var signature = method.Signature;
        if (signature.ReturnSignatureType.StackKind != CliValueKind.ValueType ||
            !body.LocalSignatureTypes.IsEmpty)
        {
            return false;
        }

        var operations = body.Instructions
            .Where(static instruction => instruction.Operation != CilOperation.Nop)
            .ToArray();
        if (operations.Length < 4 ||
            operations[^2] is not
            {
                Operation: CilOperation.LoadArgument,
                Operand: CilOperand.Index returnIndex,
            } ||
            !IsAggregateParameter(signature, returnIndex.Value) ||
            !signature.ParameterSignatureTypes[returnIndex.Value].Equals(
                signature.ReturnSignatureType))
        {
            return false;
        }

        var hasAddressProjection = false;
        var hasCall = false;
        foreach (var instruction in operations[..^2])
        {
            switch (instruction)
            {
                case
                {
                    Operation: CilOperation.LoadArgumentAddress,
                    Operand: CilOperand.Index index,
                } when IsAggregateParameter(signature, index.Value):
                    hasAddressProjection = true;
                    break;
                case { Operation: CilOperation.LoadInt32 }:
                    break;
                case { Operation: CilOperation.Call }:
                    hasCall = true;
                    break;
                default:
                    return false;
            }
        }

        return hasAddressProjection && hasCall;
    }

    private static bool IsAggregateParameter(
        MethodSignatureModel signature,
        int index) =>
        (uint)index < (uint)signature.ParameterSignatureTypes.Length &&
        signature.ParameterSignatureTypes[index].StackKind == CliValueKind.ValueType;

    private static bool IsSupportedBody(
        MethodInstanceModel method,
        CilMethodBody body)
    {
        if (body.Method.Key != method.Definition.Key ||
            !body.ExceptionRegions.IsEmpty ||
            body.Instructions.IsEmpty ||
            body.Instructions[^1].Operation != CilOperation.Return ||
            body.LocalSignatureTypes.Length != body.Locals.Length)
        {
            return false;
        }

        var offsets = body.Instructions
            .Select(static instruction => instruction.Offset)
            .ToHashSet();
        return body.Instructions.All(instruction =>
            instruction.Operation is not (
                CilOperation.Throw or
                CilOperation.Rethrow or
                CilOperation.Leave or
                CilOperation.EndFinally or
                CilOperation.EndFilter or
                CilOperation.LocalAllocate) &&
            HasValidTargets(instruction.Operand, offsets));
    }

    private static bool HasValidTargets(
        CilOperand operand,
        HashSet<int> offsets) => operand switch
        {
            CilOperand.BranchTarget target => offsets.Contains(target.Offset),
            CilOperand.SwitchTargets targets =>
                targets.Offsets.All(offsets.Contains),
            _ => true,
        };

    private static bool IsReferenceFieldAccessor(CilMethodBody body)
    {
        var operations = body.Instructions
            .Where(static instruction => instruction.Operation != CilOperation.Nop)
            .ToArray();
        return operations is
        [
        {
            Operation: CilOperation.LoadArgument,
            Operand: CilOperand.Index { Value: 0 },
        },
        { Operation: CilOperation.LoadField or CilOperation.LoadFieldAddress },
        { Operation: CilOperation.Return },
        ];
    }
}
