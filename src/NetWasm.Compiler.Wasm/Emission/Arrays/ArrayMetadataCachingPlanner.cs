using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal sealed class ArrayMetadataCachingPlanner(
    IArgumentSignatureTypeResolver arguments) : IArrayMetadataCachingPlanner
{
    private readonly IArgumentSignatureTypeResolver _arguments = arguments ??
        throw new ArgumentNullException(nameof(arguments));

    public ArrayMetadataCachingPlan Plan(StructuredMethod method, bool enabled, bool hoistNullChecks = false)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!enabled || !method.ExceptionGroups.IsEmpty)
        {
            return ArrayMetadataCachingPlan.Empty;
        }

        var loopOffsets = new HashSet<int>();
        var loopEntries = new Dictionary<StructuredBlockId, HashSet<int>>();
        var preTested = new HashSet<StructuredBlockId>();
        CollectLoopOffsets(method.Body, method, false, loopOffsets, loopEntries, preTested);
        if (loopOffsets.Count == 0)
        {
            return ArrayMetadataCachingPlan.Empty;
        }

        var candidates = new HashSet<ArrayReferenceSlot>();
        foreach (var instruction in method.Header.Instructions)
        {
            if (instruction.Operation is not (CilOperation.LoadLocal or CilOperation.LoadArgument))
            {
                continue;
            }
            var slot = new ArrayReferenceSlot(
                instruction.Operation == CilOperation.LoadArgument,
                CilOperandReader.GetIndex(instruction));
            var type = slot.IsArgument
                ? _arguments.Resolve(method.Header, slot.Index)
                : method.Header.LocalSignatureTypes[slot.Index];
            if (type.Shape == CliTypeShape.SzArray)
            {
                candidates.Add(slot);
            }
        }
        foreach (var instruction in method.Header.Instructions)
        {
            if (instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress ||
                loopOffsets.Contains(instruction.Offset) &&
                instruction.Operation is CilOperation.StoreLocal or CilOperation.StoreArgument)
            {
                candidates.Remove(new(
                    instruction.Operation is CilOperation.LoadArgumentAddress or CilOperation.StoreArgument,
                    CilOperandReader.GetIndex(instruction)));
            }
        }

        var accesses = new Dictionary<int, ArrayReferenceSlot>();
        foreach (var block in method.Blocks.Values)
        {
            var origins = new List<ArrayReferenceSlot?>();
            foreach (var instruction in block.Instructions)
            {
                if (!method.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack))
                {
                    origins.Clear();
                    continue;
                }
                if (origins.Count != stack.Length)
                {
                    origins.Clear();
                    origins.AddRange(Enumerable.Repeat<ArrayReferenceSlot?>(null, stack.Length));
                }
                Track(instruction, origins, candidates, accesses);
            }
        }

        var selected = accesses.Where(pair => loopOffsets.Contains(pair.Key))
            .Select(pair => pair.Value).ToImmutableHashSet();
        if (selected.IsEmpty)
        {
            return ArrayMetadataCachingPlan.Empty;
        }
        var selectedAccesses = accesses.Where(pair => selected.Contains(pair.Value)).ToImmutableDictionary();
        var entryChecks = hoistNullChecks
            ? EntryNullChecks(method, preTested, selectedAccesses)
            : [];
        return new(
            [.. selected.OrderBy(slot => slot.IsArgument).ThenBy(slot => slot.Index)],
            selectedAccesses)
        {
            LoopEntries = loopEntries.ToImmutableDictionary(
                pair => pair.Key,
                pair => accesses.Where(access => pair.Value.Contains(access.Key) && selected.Contains(access.Value))
                    .Select(access => access.Value).Distinct()
                    .OrderBy(slot => slot.IsArgument).ThenBy(slot => slot.Index).ToImmutableArray()),
            EntryNullChecks = entryChecks,
            // Candidates are never stored inside a loop, so a slot checked on
            // entry stays non-null for every access in that loop.
            CheckedAccesses = [.. entryChecks.SelectMany(entry => selectedAccesses
                .Where(access => loopEntries[entry.Key].Contains(access.Key) && entry.Value.Contains(access.Value))
                .Select(access => access.Key))],
        };
    }

    // Instructions with no effect a caller could observe once a null check
    // ahead of them throws: the method has no handlers, so a store to a local
    // is lost with the frame.
    private static readonly FrozenSet<CilOperation> Unobservable = new[]
    {
        CilOperation.Nop, CilOperation.LoadLocal, CilOperation.LoadArgument, CilOperation.StoreLocal,
        CilOperation.StoreArgument, CilOperation.LoadInt32, CilOperation.LoadInt64, CilOperation.LoadFloat32,
        CilOperation.LoadFloat64, CilOperation.LoadNull, CilOperation.Duplicate, CilOperation.Pop,
        CilOperation.Add, CilOperation.Subtract, CilOperation.Multiply, CilOperation.BitwiseAnd,
        CilOperation.BitwiseOr, CilOperation.BitwiseXor, CilOperation.ShiftLeft, CilOperation.ShiftRightSigned,
        CilOperation.ShiftRightUnsigned, CilOperation.Negate, CilOperation.OnesComplement,
        CilOperation.CompareEqual, CilOperation.CompareGreaterThanSigned, CilOperation.CompareGreaterThanUnsigned,
        CilOperation.CompareLessThanSigned, CilOperation.CompareLessThanUnsigned, CilOperation.ConvertInt32,
        CilOperation.ConvertInt32Unsigned, CilOperation.ConvertInt64, CilOperation.ConvertInt64Unsigned,
        CilOperation.ConvertNativeInt, CilOperation.ConvertNativeUInt,
    }.ToFrozenSet();

    // A pre-test loop evaluates its condition first on entry. When the
    // condition's first effect is the null check of a cached array, the loop
    // entry can make that check instead: it throws exactly when the first
    // evaluation would. A length read has no other effect, so the next one
    // can move too; an element access may throw something else first.
    private static ImmutableDictionary<StructuredBlockId, ImmutableArray<ArrayReferenceSlot>> EntryNullChecks(
        StructuredMethod method,
        HashSet<StructuredBlockId> preTested,
        ImmutableDictionary<int, ArrayReferenceSlot> accesses)
    {
        var checks = ImmutableDictionary.CreateBuilder<StructuredBlockId, ImmutableArray<ArrayReferenceSlot>>();
        foreach (var condition in preTested)
        {
            var slots = ImmutableArray.CreateBuilder<ArrayReferenceSlot>();
            foreach (var instruction in method.Blocks[condition].Instructions)
            {
                if (accesses.TryGetValue(instruction.Offset, out var slot))
                {
                    if (!slots.Contains(slot)) slots.Add(slot);
                    if (instruction.Operation == CilOperation.LoadArrayLength) continue;
                    break;
                }
                if (!Unobservable.Contains(instruction.Operation)) break;
            }
            if (slots.Count != 0) checks.Add(condition, slots.ToImmutable());
        }
        return checks.ToImmutable();
    }

    private static void Track(
        CilInstruction instruction,
        List<ArrayReferenceSlot?> origins,
        HashSet<ArrayReferenceSlot> candidates,
        Dictionary<int, ArrayReferenceSlot> accesses)
    {
        var operation = instruction.Operation;
        switch (operation)
        {
            case CilOperation.LoadLocal:
            case CilOperation.LoadArgument:
                var slot = new ArrayReferenceSlot(
                    operation == CilOperation.LoadArgument, CilOperandReader.GetIndex(instruction));
                origins.Add(candidates.Contains(slot) ? slot : null);
                return;
            case CilOperation.StoreLocal:
            case CilOperation.StoreArgument:
                var assigned = new ArrayReferenceSlot(
                    operation == CilOperation.StoreArgument, CilOperandReader.GetIndex(instruction));
                Consume(origins, 1, false);
                // A reference already on the stack still denotes the old array.
                for (var index = 0; index < origins.Count; index++)
                {
                    if (origins[index] == assigned) origins[index] = null;
                }
                return;
            case CilOperation.Duplicate:
                origins.Add(origins[^1]);
                return;
            case CilOperation.Nop:
            case CilOperation.Readonly:
            case CilOperation.Volatile:
            case CilOperation.Unaligned:
                return;
            case CilOperation.LoadArrayLength:
                RecordAccess(1);
                Consume(origins, 1, true);
                return;
            case CilOperation.LoadArrayElement:
            case CilOperation.LoadArrayElementReference:
            case CilOperation.LoadArrayElementAddress:
                RecordAccess(2);
                Consume(origins, 2, true);
                return;
            case CilOperation.StoreArrayElement:
            case CilOperation.StoreArrayElementReference:
                RecordAccess(3);
                Consume(origins, 3, false);
                return;
            case CilOperation.Pop:
            case CilOperation.StoreStaticField:
                Consume(origins, 1, false);
                return;
            case CilOperation.StoreField:
            case CilOperation.StoreObject:
                Consume(origins, 2, false);
                return;
            case CilOperation.LoadField:
            case CilOperation.LoadFieldAddress:
            case CilOperation.LoadObject:
            case CilOperation.Negate:
            case CilOperation.OnesComplement:
            case >= CilOperation.ConvertInt32 and <= CilOperation.ConvertNumeric:
                Consume(origins, 1, true);
                return;
            case >= CilOperation.Add and <= CilOperation.RemainderUnsigned:
            case >= CilOperation.CompareEqual and <= CilOperation.CompareLessThanUnsigned:
                Consume(origins, 2, true);
                return;
            case >= CilOperation.LoadInt32 and <= CilOperation.LoadFieldToken:
            case CilOperation.LoadLocalAddress:
            case CilOperation.LoadArgumentAddress:
            case CilOperation.LoadStaticField:
            case CilOperation.LoadStaticFieldAddress:
            case CilOperation.SizeOf:
                origins.Add(null);
                return;
            default:
                // Unknown stack effects cannot establish an array origin.
                origins.Clear();
                return;
        }

        void RecordAccess(int consumed)
        {
            if (origins.Count >= consumed && origins[^consumed] is { } origin)
            {
                accesses.Add(instruction.Offset, origin);
            }
        }
    }

    private static void Consume(List<ArrayReferenceSlot?> origins, int count, bool produces)
    {
        origins.RemoveRange(origins.Count - count, count);
        if (produces) origins.Add(null);
    }

    private static void CollectLoopOffsets(
        StructuredSequence sequence,
        StructuredMethod method,
        bool repeating,
        HashSet<int> offsets,
        Dictionary<StructuredBlockId, HashSet<int>> loopEntries,
        HashSet<StructuredBlockId> preTested)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredCode code:
                    Add(code.Occurrence, repeating);
                    break;
                case StructuredIf conditional:
                    Add(conditional.Condition, repeating);
                    CollectLoopOffsets(conditional.WhenTrue, method, repeating, offsets, loopEntries, preTested);
                    CollectLoopOffsets(conditional.WhenFalse, method, repeating, offsets, loopEntries, preTested);
                    break;
                case StructuredLoop { Condition: { } condition } loop:
                    preTested.Add(condition.Block);
                    AddLoop(condition, loop.Body, loop.ContinueBody);
                    CollectLoopOffsets(loop.ExitBody, method, repeating, offsets, loopEntries, preTested);
                    break;
                case StructuredPostTestLoop loop:
                    AddLoop(loop.Condition, loop.Body, loop.ContinueBody);
                    CollectLoopOffsets(loop.ExitBody, method, repeating, offsets, loopEntries, preTested);
                    break;
                case StructuredLoop loop:
                    // Like a dispatcher, an unconditional loop has no entry
                    // condition to snapshot metadata before; its body repeats.
                    CollectLoopOffsets(loop.Body, method, true, offsets, loopEntries, preTested);
                    break;
                case StructuredSequence nested:
                    CollectLoopOffsets(nested, method, repeating, offsets, loopEntries, preTested);
                    break;
                case StructuredDispatcher dispatcher:
                    foreach (var block in dispatcher.Blocks) Add(block.Occurrence, true);
                    foreach (var exit in dispatcher.Exits)
                        CollectLoopOffsets(exit.Body, method, repeating, offsets, loopEntries, preTested);
                    break;
            }
        }

        void Add(StructuredBlockOccurrence occurrence, bool inLoop)
        {
            if (inLoop)
                offsets.UnionWith(method.Blocks[occurrence.Block].Instructions.Select(value => value.Offset));
        }

        void AddLoop(
            StructuredBlockOccurrence condition,
            StructuredSequence body,
            StructuredSequence continuation)
        {
            var current = method.Blocks[condition.Block].Instructions.Select(value => value.Offset).ToHashSet();
            CollectLoopOffsets(body, method, true, current, loopEntries, preTested);
            CollectLoopOffsets(continuation, method, true, current, loopEntries, preTested);
            offsets.UnionWith(current);
            if (loopEntries.TryGetValue(condition.Block, out var existing)) existing.UnionWith(current);
            else loopEntries.Add(condition.Block, current);
        }
    }
}
