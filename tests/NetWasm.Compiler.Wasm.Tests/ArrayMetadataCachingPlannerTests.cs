using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ArrayMetadataCachingPlannerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReassignedLoopStorageCannotSupplyCachedMetadata(bool argument)
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadNull, new CilOperand.None()),
            Step(argument ? CilOperation.StoreArgument : CilOperation.StoreLocal,
                new CilOperand.Index(0), CliValueKind.ManagedReference));
        var load = method.Header.Instructions[0] with
        {
            Operation = argument ? CilOperation.LoadArgument : CilOperation.LoadLocal,
        };
        method = method with
        {
            Header = method.Header with
            {
                Method = method.Header.Method with
                {
                    Signature = MethodSignatureModel.Create(CliValueKind.I4) with
                    {
                        ParameterTypes = [CliValueKind.ManagedReference],
                        ParameterSignatureTypes = [method.Header.LocalSignatureTypes[0]],
                    },
                },
                Instructions = method.Header.Instructions.SetItem(0, load),
            },
            Blocks = method.Blocks.SetItem(new(0), method.Blocks[new(0)] with
            {
                Instructions = method.Blocks[new(0)].Instructions.SetItem(0, load),
            }),
            Body = new([new StructuredLoop(new(new(0), StructuredBlockRole.Owner), true,
                new([new StructuredCode(new(new(1), StructuredBlockRole.Owner))]),
                StructuredSequence.Empty, StructuredSequence.Empty)]),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Same(ArrayMetadataCachingPlan.Empty, plan);
    }

    [Fact]
    public void AssignmentDoesNotTurnAnUnknownStackReferenceIntoACachedOrigin()
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadNull, new CilOperand.None()),
            Step(CilOperation.LoadLocal, new CilOperand.Index(0), CliValueKind.ManagedReference),
            Step(CilOperation.StoreLocal, new CilOperand.Index(0),
                CliValueKind.ManagedReference, CliValueKind.ManagedReference),
            Step(CilOperation.LoadArrayLength, new CilOperand.None(), CliValueKind.ManagedReference));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.DoesNotContain(23, plan.Accesses.Keys);
        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
    }

    [Fact]
    public void BinaryIndexArithmeticPreservesTheArrayBelowItsOperands()
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.ManagedReference),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(1), CliValueKind.ManagedReference, CliValueKind.I4),
            Step(CilOperation.Add, new CilOperand.None(), CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4),
            Step(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                CliValueKind.ManagedReference, CliValueKind.I4));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[24]);
    }

    [Fact]
    public void UnknownCallEffectsDoNotRecoverOriginsFromStackDepth()
    {
        var assembly = new AssemblyIdentity("array-metadata-tests");
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var definition = new MethodDefinitionModel(new(assembly, 0x06000002), new(assembly, 0x02000001),
            "Other", true, signature, 1);
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.Call, new CilOperand.MethodInstance(new(definition,
                CliTypeIdentity.Named(assembly, "Tests", "Owner", false), [], signature)), CliValueKind.ManagedReference),
            Step(CilOperation.LoadArrayLength, new CilOperand.None(), CliValueKind.ManagedReference));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.DoesNotContain(22, plan.Accesses.Keys);
        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectAndFieldStoresPreserveTheArrayBelowTheirOperands(bool fieldStore)
    {
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var assembly = new AssemblyIdentity("array-metadata-tests");
        var cell = fieldStore ? CliTypeIdentity.Named(assembly, "Tests", "Cell", true) : scalar;
        CilOperand operand = fieldStore
            ? new CilOperand.FieldInstance(new(new(new(assembly, 0x04000001), new(assembly, 0x02000002),
                "Value", scalar, false), cell, scalar))
            : new CilOperand.TypeIdentity(scalar);
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadLocalAddress, new CilOperand.Index(2), CliValueKind.ManagedReference),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(7), CliValueKind.ManagedReference, CliValueKind.ManagedAddress),
            Step(fieldStore ? CilOperation.StoreField : CilOperation.StoreObject, operand,
                CliValueKind.ManagedReference, CliValueKind.ManagedAddress, CliValueKind.I4),
            Step(CilOperation.LoadArrayLength, new CilOperand.None(), CliValueKind.ManagedReference));
        method = method with
        {
            Header = method.Header with
            {
                Locals = method.Header.Locals.Add(cell.StackKind),
                LocalSignatureTypes = method.Header.LocalSignatureTypes.Add(cell),
            },
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[24]);
    }

    [Theory]
    [InlineData(CilOperation.LoadArrayElement, CliValueKind.I4)]
    [InlineData(CilOperation.LoadArrayElementReference, CliValueKind.ManagedReference)]
    [InlineData(CilOperation.LoadArrayElementAddress, CliValueKind.ManagedAddress)]
    public void ElementLoadsReuseTheKnownArraySnapshot(CilOperation operation, CliValueKind result)
    {
        var element = CliTypeIdentity.FromStackKind(operation == CilOperation.LoadArrayElementReference
            ? CliValueKind.ManagedReference : CliValueKind.I4);
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.ManagedReference),
            Step(operation, new CilOperand.TypeIdentity(element),
                CliValueKind.ManagedReference, CliValueKind.I4),
            Step(CilOperation.Pop, new CilOperand.None(), result));
        method = method with
        {
            Header = method.Header with
            {
                LocalSignatureTypes = method.Header.LocalSignatureTypes.SetItem(0, CliTypeIdentity.SzArray(element)),
            },
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[22]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ElementStoresLocateTheArrayBelowIndexAndValue(bool reference)
    {
        var value = reference ? CliValueKind.ManagedReference : CliValueKind.I4;
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.ManagedReference),
            Step(reference ? CilOperation.LoadNull : CilOperation.LoadInt32,
                reference ? new CilOperand.None() : new CilOperand.ConstantI4(7),
                CliValueKind.ManagedReference, CliValueKind.I4),
            Step(reference ? CilOperation.StoreArrayElementReference : CilOperation.StoreArrayElement,
                new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(value)),
                CliValueKind.ManagedReference, CliValueKind.I4, value));
        method = method with
        {
            Header = method.Header with
            {
                LocalSignatureTypes = method.Header.LocalSignatureTypes.SetItem(0,
                    CliTypeIdentity.SzArray(CliTypeIdentity.FromStackKind(value))),
            },
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[23]);
    }

    [Theory]
    [InlineData(CilOperation.Nop)]
    [InlineData(CilOperation.Readonly)]
    [InlineData(CilOperation.Volatile)]
    [InlineData(CilOperation.Unaligned)]
    public void StackNeutralOperationsPreserveKnownArrayOrigins(CilOperation operation)
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(operation, new CilOperand.None(), CliValueKind.ManagedReference),
            Step(CilOperation.LoadArrayLength, new CilOperand.None(), CliValueKind.ManagedReference));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[22]);
    }

    [Theory]
    [InlineData(CilOperation.Negate)]
    [InlineData(CilOperation.OnesComplement)]
    [InlineData(CilOperation.ConvertInt32)]
    public void IndexTransformationsDoNotEraseTheArrayBelowThem(CilOperation operation)
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.ManagedReference),
            Step(operation, new CilOperand.None(), CliValueKind.ManagedReference, CliValueKind.I4),
            Step(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                CliValueKind.ManagedReference, CliValueKind.I4));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[23]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void StoreInvalidatesOnlyStackOriginsOfTheAssignedLocal(int assignedLocal, bool retained)
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.Duplicate, new CilOperand.None(), CliValueKind.ManagedReference),
            Step(CilOperation.StoreLocal, new CilOperand.Index(assignedLocal),
                CliValueKind.ManagedReference, CliValueKind.ManagedReference),
            Step(CilOperation.LoadArrayLength, new CilOperand.None(), CliValueKind.ManagedReference));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(retained, plan.Accesses.ContainsKey(23));
        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrayArgumentsRequireUnexposedReferenceStorage(bool exposed)
    {
        var method = Create(false);
        var load = method.Header.Instructions[0] with { Operation = CilOperation.LoadArgument };
        var instructions = method.Header.Instructions.SetItem(0, load);
        if (exposed)
            instructions = instructions.Add(new(20, 21, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)));
        method = method with
        {
            Header = method.Header with
            {
                Method = method.Header.Method with
                {
                    Signature = MethodSignatureModel.Create(CliValueKind.I4) with
                    {
                        ParameterTypes = [CliValueKind.ManagedReference],
                        ParameterSignatureTypes = [method.Header.LocalSignatureTypes[0]],
                    },
                },
                Instructions = instructions,
            },
            Blocks = method.Blocks.SetItem(new(0), method.Blocks[new(0)] with
            {
                Instructions = method.Blocks[new(0)].Instructions.SetItem(0, load),
            }),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        if (exposed)
            Assert.Same(ArrayMetadataCachingPlan.Empty, plan);
        else
            Assert.Equal([new ArrayReferenceSlot(true, 0)], plan.Slots.ToArray());
    }

    [Fact]
    public void LoopsInsideConditionalsRetainTheirSnapshotRequirements()
    {
        var method = Create(false);
        method = method with
        {
            Body = new([new StructuredIf(new(new(0), StructuredBlockRole.ExecutingReplica),
                method.Body, StructuredSequence.Empty)]),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
        Assert.Equal(plan.Slots.ToArray(), plan.LoopEntries[new(0)].ToArray());
    }

    [Fact]
    public void DispatcherBlocksAreRepeatingButTheirExitIsNot()
    {
        var method = Create(false);
        method = method with
        {
            Body = new([new StructuredDispatcher(new(0),
                [new(new(new(0), StructuredBlockRole.Owner), null, null)],
                [new(new(1), new([new StructuredCode(new(new(1), StructuredBlockRole.Owner))]))])]),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
        Assert.Empty(plan.LoopEntries);
    }

    [Fact]
    public void RejectsMissingResolverAndMethod()
    {
        Assert.Throws<ArgumentNullException>(() => new ArrayMetadataCachingPlanner(null!));
        Assert.Throws<ArgumentNullException>(() => new ArrayMetadataCachingPlanner(new Arguments()).Plan(null!, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MissingStackFactsCannotEstablishALoopSnapshot(int offset)
    {
        var method = Create(false);
        method = method with { InstructionEntryStacks = method.InstructionEntryStacks.Remove(offset) };

        Assert.Same(ArrayMetadataCachingPlan.Empty,
            new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true));
    }

    [Fact]
    public void NestedSequencesPreserveLoopEntrySelection()
    {
        var method = Create(false);
        method = method with { Body = new([method.Body]) };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
        Assert.Equal(plan.Slots.ToArray(), plan.LoopEntries[new(0)].ToArray());
    }

    [Fact]
    public void RepeatedLoopConditionUnionsTheAccessesOfBothBodies()
    {
        var method = Create(false);
        var second = new StructuredLoop(new(new(0), StructuredBlockRole.ExecutingReplica), true,
            new([new StructuredCode(new(new(1), StructuredBlockRole.ExecutingReplica))]),
            StructuredSequence.Empty, StructuredSequence.Empty);
        method = method with { Body = new([method.Body.Regions[0], second]) };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal([new ArrayReferenceSlot(false, 0), new ArrayReferenceSlot(false, 1)], plan.Slots.ToArray());
        Assert.Equal(plan.Slots.ToArray(), plan.LoopEntries[new(0)].ToArray());
    }

    [Fact]
    public void NonrepeatingCodeDoesNotCreateSnapshots()
    {
        var method = Create(false);
        method = method with
        {
            Body = new([new StructuredCode(new(new(0), StructuredBlockRole.Owner)),
                new StructuredCode(new(new(1), StructuredBlockRole.Owner))]),
        };

        Assert.Same(ArrayMetadataCachingPlan.Empty,
            new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectsOnlyLoopUsedSlotsAtEntry(bool postTest)
    {
        var method = Create(postTest);
        var planner = new[] { new ArrayMetadataCachingPlanner(new Arguments()) }
            .Cast<IArrayMetadataCachingPlanner>().Single();

        var plan = planner.Plan(method, true);

        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
        Assert.Equal(plan.Slots.ToArray(), plan.LoopEntries[new(0)].ToArray());
        Assert.DoesNotContain(new ArrayReferenceSlot(false, 1), plan.Slots);
    }

    [Fact]
    public void ExposedReferenceHasNoSnapshots()
    {
        var method = Create(false);
        method = method with
        {
            Header = method.Header with
            {
                Instructions = method.Header.Instructions.Add(new(20, 21,
                CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
            }
        };

        Assert.Same(ArrayMetadataCachingPlan.Empty,
            new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true));
    }

    [Fact]
    public void DisabledPolicyHasNoEntryRefreshes() => Assert.Empty(
        new ArrayMetadataCachingPlanner(new Arguments()).Plan(Create(false), false).LoopEntries);

    private static InstructionStep Step(CilOperation operation, CilOperand operand,
        params CliValueKind[] stack) => new(operation, operand, [.. stack]);

    private readonly record struct InstructionStep(CilOperation Operation, CilOperand Operand,
        ImmutableArray<CliValueKind> Stack);

    private static StructuredMethod WithOutsideInstructions(params InstructionStep[] steps)
    {
        var method = Create(false);
        var instructions = steps.Select((step, index) =>
            new CilInstruction(20 + index, 21 + index, step.Operation, step.Operand)).ToImmutableArray();
        return method with
        {
            Header = method.Header with
            {
                MaxStack = 8,
                Instructions = [.. method.Blocks[new(0)].Instructions, .. instructions],
            },
            Blocks = method.Blocks.SetItem(new(1), method.Blocks[new(1)] with
            {
                StartOffset = 20,
                EndOffset = 20 + steps.Length,
                Instructions = instructions,
            }),
            InstructionEntryStacks = method.InstructionEntryStacks.Remove(2).Remove(3)
                .AddRange(steps.Select((step, index) => new KeyValuePair<int, ImmutableArray<CliValueKind>>(20 + index, step.Stack))),
        };
    }

    private static StructuredMethod Create(bool postTest)
    {
        var assembly = new AssemblyIdentity("array-metadata-tests");
        var definition = new MethodDefinitionModel(new(assembly, 0x06000001), new(assembly, 0x02000001),
            "Body", true, MethodSignatureModel.Create(CliValueKind.I4), 1);
        var array = CliTypeIdentity.SzArray(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        ImmutableArray<CilInstruction> instructions =
        [
            new(0, 1, CilOperation.LoadLocal, new CilOperand.Index(0)),
            new(1, 2, CilOperation.LoadArrayLength, new CilOperand.None()),
            new(2, 3, CilOperation.LoadLocal, new CilOperand.Index(1)),
            new(3, 4, CilOperation.LoadArrayLength, new CilOperand.None()),
        ];
        var condition = new StructuredBlockOccurrence(new(0), StructuredBlockRole.Owner);
        var exit = new StructuredSequence([new StructuredCode(new(new(1), StructuredBlockRole.Owner))]);
        StructuredRegion loop = postTest
            ? new StructuredPostTestLoop(StructuredSequence.Empty, condition, true, StructuredSequence.Empty, exit)
            : new StructuredLoop(condition, true, StructuredSequence.Empty, StructuredSequence.Empty, exit);
        return new(new(definition, null, 1, [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
                [array, array], instructions), new(0),
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                .Add(new(0), new(new(0), 0, instructions[..2], [], new StructuredFallthroughExit(null)) { EndOffset = 2 })
                .Add(new(1), new(new(1), 2, instructions[2..], [], new StructuredFallthroughExit(null)) { EndOffset = 4 }),
            new([loop]), [], [],
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty
                .Add(0, []).Add(1, [CliValueKind.ManagedReference])
                .Add(2, []).Add(3, [CliValueKind.ManagedReference]));
    }

    private sealed class Arguments : IArgumentSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(StructuredMethodHeader header, int index) =>
            header.Method.Signature.ParameterSignatureTypes[index];
    }
}
