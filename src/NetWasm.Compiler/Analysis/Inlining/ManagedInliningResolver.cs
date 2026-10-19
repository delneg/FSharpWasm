using System;
using System.Collections.Concurrent;
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
    IMethodInitializationTriggerClassifier initializationTriggers,
    IRuntimeIntrinsicRegistry intrinsics,
    int smallBodyInstructionLimit) : IManagedInliningResolver
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
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));
    private readonly ManagedMethodInliningPolicy _policy = policy switch
    {
        ManagedMethodInliningPolicy.Disabled or
        ManagedMethodInliningPolicy.Speed => policy,
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };
    private readonly int _smallBodyInstructionLimit = smallBodyInstructionLimit >= 0
        ? smallBodyInstructionLimit
        : throw new ArgumentOutOfRangeException(nameof(smallBodyInstructionLimit));
    // Whether a callee qualifies depends only on the callee, so each is examined once.
    private readonly ConcurrentDictionary<string, Candidate?> _candidates = new(StringComparer.Ordinal);

    // .NET's JIT always inlines a legal callee of up to 16 IL bytes, about
    // eight instructions. O3 trades size for speed, and sixteen measured faster
    // under Wasmtime than eight.
    internal const int SmallBodyInstructionLimit = 16;

    public ManagedInliningPlan? Resolve(ManagedInliningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_policy == ManagedMethodInliningPolicy.Disabled ||
            request.Call.Operation is not (CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject) ||
            _calledMethods.Resolve(request.Call) is not { } method ||
            !method.Definition.HasManagedBody ||
            method.Definition.IsNoInlining ||
            // The compiler replaces these calls; their bodies are placeholders or stubs.
            _intrinsics.TryGetIntrinsic(method.Definition.Key, out _) ||
            method.Definition.JSImport is not null || method.Definition.WitImport is not null ||
            method.Definition.NativeImport is not null ||
            _initializationTriggers.Classify(method.Definition) ||
            method.CanonicalName == request.Caller.CanonicalName ||
            !HasSupportedCallMetadata(request.Call, method))
        {
            return null;
        }

        var candidate = _candidates.GetOrAdd(method.CanonicalName, _ => Examine(method));
        // The priority shapes are unbounded in size, so calls exposed by an
        // earlier round take only small and explicitly aggressive callees, and
        // not allocating ones: a lazily initialized getter inlined through its
        // allocating initializer put that cold path into every caller.
        if (candidate is null ||
            !(candidate.Small && !(request.Nested && candidate.Allocates) ||
              (request.Nested ? method.Definition.IsAggressiveInlining : candidate.Priority)) ||
            // A callvirt checks its receiver for null before the callee runs.
            !method.Definition.IsStatic && request.Call.Operation == CilOperation.CallVirtual &&
            !DereferencesReceiverFirst(candidate.Plan.Body))
        {
            return null;
        }
        return candidate.Plan;
    }

    private Candidate? Examine(MethodInstanceModel method)
    {
        var body = _specializer.Rewrite(_methodBodies.ReadMethodBody(method)) with
        {
            MethodInstance = method,
        };
        if (!IsSupportedBody(method, body))
        {
            return null;
        }
        var priority = method.Definition.IsStatic
            ? IsPriorityCandidate(method, body)
            : method.Definition.IsAggressiveInlining || IsReferenceFieldAccessor(body);
        var small = IsSmall(body);
        return priority || small
            ? new(new(method, body) { InstructionEntryStacks = _bodyValidator.Validate(body) }, priority, small,
                Enumerable.Range(0, body.Instructions.Length).Any(index => Allocates(body, index)))
            : null;
    }

    private sealed record Candidate(ManagedInliningPlan Plan, bool Priority, bool Small, bool Allocates);

    // A wrapper of a large explicit priority callee is as large as that callee
    // once the priority tier composes it, so it does not count as small. A
    // small callee composes like any other small callee in a later round, and
    // a call that the compiler replaces with instructions stays small.
    private bool IsSmall(CilMethodBody body) =>
        CountsAsSmall(body) &&
        !body.Instructions.Any(instruction => instruction.Operation is CilOperation.Call or CilOperation.CallVirtual &&
            _calledMethods.Resolve(instruction) is { } target && IsExplicitPriorityCandidate(target) &&
            !_intrinsics.TryGetIntrinsic(target.Definition.Key, out _) &&
            !(target.Definition.HasManagedBody && CountsAsSmall(_methodBodies.ReadMethodBody(target))));

    private bool CountsAsSmall(CilMethodBody body) =>
        body.Instructions.Count(static instruction => instruction.Operation != CilOperation.Nop) <= _smallBodyInstructionLimit;

    private bool Allocates(CilMethodBody body, int index) => body.Instructions[index].Operation switch
    {
        // Constructing a value type initializes storage in place, and an
        // exception constructed for a throw is the cold path. A supported
        // body ends in a return, so an instruction follows.
        CilOperation.NewObject => _calledMethods.Resolve(body.Instructions[index]) is not { DeclaringType.IsValueType: true } &&
            body.Instructions[index + 1].Operation != CilOperation.Throw,
        CilOperation.NewArray or CilOperation.NewRectangularArray or CilOperation.Box => true,
        _ => false,
    };

    // Inlined code keeps the callvirt null check's order only when its first
    // effect, after pure pushes, dereferences the receiver.
    private static bool DereferencesReceiverFirst(CilMethodBody body)
    {
        var operations = body.Instructions.Where(static instruction => instruction.Operation != CilOperation.Nop).ToArray();
        // A supported body ends in ret, so an effect always exists.
        var effect = Array.FindIndex(operations, static instruction =>
            instruction.Operation is not (CilOperation.LoadArgument or CilOperation.LoadInt32 or CilOperation.LoadNull));
        return operations[effect].Operation switch
        {
            CilOperation.LoadField or CilOperation.LoadFieldAddress => IsReceiver(operations, effect - 1),
            CilOperation.StoreField => IsReceiver(operations, effect - 2),
            _ => false,
        };
    }

    private static bool IsReceiver(CilInstruction[] operations, int index) =>
        index >= 0 && operations[index] is { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 } };

    private static bool HasSupportedCallMetadata(
        CilInstruction call,
        MethodInstanceModel method)
    {
        if (method.Definition.IsStatic)
        {
            return call.Operation == CilOperation.Call;
        }
        // Constructing a value type initializes a local; an object needs an allocation first.
        if (call.Operation == CilOperation.NewObject)
        {
            return method.DeclaringType.IsValueType;
        }

        // A value-type instance is passed by address to a direct call.
        return method.DeclaringType.IsValueType
            ? call.Operation == CilOperation.Call
            : call.Operation == CilOperation.Call ||
                !method.Definition.IsVirtual ||
                method.Definition.IsFinal;
    }

    private static bool IsPriorityCandidate(
        MethodInstanceModel method,
        CilMethodBody body) =>
        IsExplicitPriorityCandidate(method) ||
        IsAggregateAddressForwardingWrapper(method, body);

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
            body.LocalSignatureTypes.Length != body.Locals.Length ||
            // A constructed method's body is decoded in its own generic
            // context, so it moves into another method only when closed.
            body.LocalSignatureTypes.Any(static type => type.ContainsGenericParameters) ||
            body.Instructions.Any(static instruction => IsOpen(instruction.Operand)))
        {
            return false;
        }

        var offsets = body.Instructions
            .Select(static instruction => instruction.Offset)
            .ToHashSet();
        return body.Instructions.All(instruction =>
            // A throw leaves the inlined body the way it leaves a call.
            instruction.Operation is not (
                CilOperation.Rethrow or
                CilOperation.Leave or
                CilOperation.EndFinally or
                CilOperation.EndFilter or
                CilOperation.LocalAllocate) &&
            HasValidTargets(instruction.Operand, offsets));
    }

    private static bool IsOpen(CilOperand operand) => operand switch
    {
        CilOperand.TypeIdentity type => type.Value.ContainsGenericParameters,
        CilOperand.MethodInstance method => method.Value.DeclaringType.ContainsGenericParameters ||
            method.Value.MethodArguments.Any(static type => type.ContainsGenericParameters),
        CilOperand.FieldInstance field => field.Value.DeclaringType.ContainsGenericParameters ||
            field.Value.FieldType.ContainsGenericParameters,
        _ => false,
    };

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
