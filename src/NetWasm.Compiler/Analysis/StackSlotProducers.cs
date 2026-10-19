using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Facts about one body: the instruction that pushed a value another
// instruction consumes, from the body's typed entry stacks, when it was pushed
// in the same block and no instruction between them reads or replaces that
// stack slot.
internal sealed record StackSlotProducers(
    CilMethodBody Body,
    IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> Stacks)
{
    private readonly HashSet<int> _entries = InstructionEntries.Of(Body);

    // Instructions that push nothing.
    private static readonly FrozenSet<CilOperation> PushNothing = new[]
    {
        CilOperation.Nop, CilOperation.StoreArgument, CilOperation.StoreLocal, CilOperation.Pop,
        CilOperation.StoreField, CilOperation.StoreStaticField, CilOperation.StoreObject, CilOperation.CopyObject,
        CilOperation.InitializeObject, CilOperation.CopyBlock, CilOperation.InitializeBlock,
        CilOperation.StoreArrayElement, CilOperation.StoreArrayElementReference, CilOperation.StoreRectangularArrayElement,
    }.ToFrozenSet();


    // Whether control can reach the instruction other than from the one before it.
    public bool Enters(int index) => _entries.Contains(Body.Instructions[index].Offset);

    // The producer of the value on top of the consumer's entry stack.
    public int? FindTop(int consumer) =>
        Stacks.TryGetValue(Body.Instructions[consumer].Offset, out var stack) && stack.Length > 0
            ? Find(consumer, stack.Length - 1)
            : null;

    // The producer of the value in `slot`, counted from the bottom of the
    // consumer's entry stack, which must be known.
    public int? Find(int consumer, int slot)
    {
        var after = Stacks[Body.Instructions[consumer].Offset].Length;
        for (var index = consumer - 1; index >= 0; index--)
        {
            // Another path could reach the consumer with a different value.
            if (_entries.Contains(Body.Instructions[index + 1].Offset) ||
                !Stacks.TryGetValue(Body.Instructions[index].Offset, out var stack))
            {
                return null;
            }
            // Depth grows by one at most per instruction, so one that starts
            // at the slot's depth pushed it, as did one that is certain to
            // push a single value just below the depth after it.
            var pushed = Pushed(Body.Instructions[index]);
            if (stack.Length == slot || pushed == 1 && after - 1 == slot)
            {
                return index;
            }
            // An instruction's lowest stack slot is the depth after it, less
            // what it pushed: one value at most, or two for dup, which reads
            // the slot below. Depth grows by one at most per instruction, so
            // the walk meets the slot's depth before any lower one.
            if (after - (pushed ?? 1) <= slot)
            {
                return null;
            }
            after = stack.Length;
        }
        return null;
    }

    // How many values an instruction pushes, where that is certain.
    private static int? Pushed(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.Duplicate } => 2,
        { Operation: var operation } when PushNothing.Contains(operation) => 0,
        { Operation: CilOperation.NewObject } => 1,
        { Operation: CilOperation.Call or CilOperation.CallVirtual, Operand: CilOperand.MethodInstance method } =>
            method.Value.Signature.ReturnType == CliValueKind.Void ? 0 : 1,
        _ => null,
    };
}
