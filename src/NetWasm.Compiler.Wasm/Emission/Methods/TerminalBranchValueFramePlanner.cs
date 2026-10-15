using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// Plans storage placement only. Root publication and exception ownership remain
// separate emission contracts; a plan does not authorize changing either one.
internal sealed class TerminalBranchValueFramePlanner : ITerminalBranchValueFramePlanner
{
    public TerminalBranchValueFramePlan? Plan(StructuredMethod method, ValueFrameLayout layout, MethodRootMap roots)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(roots);

        if (layout.Size == 0 || !method.ExceptionGroups.IsEmpty ||
            !layout.ArgumentOffsets.IsEmpty || !layout.NativeArgumentOffsets.IsEmpty ||
            !layout.MemberResultOffsets.IsEmpty || !layout.CallReceiverOffsets.IsEmpty ||
            method.Body.Regions.Length < 2 || method.Body.Regions[0] is not StructuredIf branch)
        {
            return null;
        }

        var tail = method.Body.Regions.Skip(1).ToImmutableArray();
        for (var index = 0; index < tail.Length; index++)
        {
            if (tail[index] is not StructuredCode code ||
                !method.Blocks[code.Occurrence.Block].EntryStack.IsEmpty)
            {
                return null;
            }
            var exit = method.Blocks[code.Occurrence.Block].Exit;
            if (index == tail.Length - 1)
            {
                if (exit is not StructuredTerminalExit
                    { Instruction.Operation: CilOperation.Return or CilOperation.Throw })
                {
                    return null;
                }
            }
            else
            {
                if (tail[index + 1] is not StructuredCode next || (exit switch
                {
                    StructuredFallthroughExit flow => flow.Target != next.Occurrence.Block,
                    StructuredBranchExit jump => jump.Target != next.Occurrence.Block,
                    _ => true,
                }))
                {
                    return null;
                }
            }
        }

        var first = new HashSet<StructuredBlockId>();
        var second = new HashSet<StructuredBlockId>();
        if (!Collect(branch.WhenTrue, first) || !Collect(branch.WhenFalse, second))
        {
            return null;
        }
        var common = tail.Cast<StructuredCode>().Select(code => code.Occurrence.Block)
            .Append(branch.Condition.Block).ToHashSet();
        var scalars = layout.SpilledScalarLocals;
        if (scalars.Any(index => !layout.LocalOffsets.ContainsKey(index) ||
                method.Header.LocalSignatureTypes[index].StackKind is not
                    (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or
                        CliValueKind.NativeInt) ||
                IsNarrowStorage(method.Header.LocalSignatureTypes[index])))
        {
            return null;
        }

        var required = new HashSet<StructuredBlockId>();
        var temporaryOffsets = layout.TemporaryOffsets.Keys.ToHashSet();
        var observedTemporaries = new HashSet<int>();
        foreach (var block in first.Concat(second).Concat(common).Distinct())
        {
            foreach (var instruction in method.Blocks[block].Instructions)
            {
                if (instruction.Operation == CilOperation.LocalAllocate)
                {
                    return null;
                }
                if (temporaryOffsets.Contains(instruction.Offset))
                {
                    observedTemporaries.Add(instruction.Offset);
                    required.Add(block);
                }
                if (instruction.Operation is not (CilOperation.LoadLocal or
                    CilOperation.StoreLocal or CilOperation.LoadLocalAddress))
                {
                    continue;
                }
                var local = CilOperandReader.GetIndex(instruction);
                if (layout.LocalOffsets.ContainsKey(local) &&
                    (!scalars.Contains(local) || instruction.Operation == CilOperation.LoadLocalAddress))
                {
                    required.Add(block);
                }
            }
        }
        if (required.Count == 0 || required.Overlaps(common) ||
            !temporaryOffsets.SetEquals(observedTemporaries))
        {
            return null;
        }
        var frameFirst = required.IsSubsetOf(first) && !required.Overlaps(second);
        var frameSecond = required.IsSubsetOf(second) && !required.Overlaps(first);
        if (!frameFirst && !frameSecond)
        {
            return null;
        }

        var unframedOffsets = (frameFirst ? second : first).Concat(common)
            .SelectMany(id =>
            {
                var block = method.Blocks[id];
                var offsets = block.Instructions.Select(instruction => instruction.Offset);
                return block.Exit switch
                {
                    StructuredTerminalExit terminal => offsets.Append(terminal.Instruction.Offset),
                    StructuredConditionalExit conditional => offsets.Append(conditional.Condition.InstructionOffset),
                    _ => offsets,
                };
            }).ToHashSet();
        if (roots.Safepoints.Values.Where(point => unframedOffsets.Contains(point.IlOffset))
            .SelectMany(point => point.Roots.Concat(point.ConstructorCallRoots))
            .Any(root => root.Kind == RootSourceKind.StackStorageAnchor ||
                root.Kind is RootSourceKind.Local or RootSourceKind.ManagedAddressLocal &&
                layout.LocalOffsets.ContainsKey(root.Index)))
        {
            return null;
        }

        // One copy retains ownership; the other is explicitly an executing
        // replica, preserving the original block-accounting contract.
        var replicaTail = tail.Cast<StructuredCode>().Select(code => (StructuredRegion)(code with
        {
            Occurrence = code.Occurrence with { Role = StructuredBlockRole.ExecutingReplica },
        }));
        return new(branch.Condition,
            new([.. branch.WhenTrue.Regions, .. tail]),
            new([.. branch.WhenFalse.Regions, .. replicaTail]),
            frameFirst,
            scalars);
    }

    private static bool IsNarrowStorage(CliTypeIdentity type) =>
        // Match the narrow sign/zero-extending loads used by ManagedMemoryEmitter.
        // Wasm stack-slot size alone does not encode those storage semantics.
        (type.StackStorageType ?? type).CanonicalName is
            "primitive:i1" or "primitive:u1" or "primitive:i2" or "primitive:u2" or
            "primitive:bool" or "primitive:char";

    private static bool Collect(StructuredRegion region, HashSet<StructuredBlockId> blocks)
    {
        IEnumerable<StructuredRegion> children;
        switch (region)
        {
            case StructuredCode code:
                blocks.Add(code.Occurrence.Block);
                return true;
            case StructuredSequence sequence:
                children = sequence.Regions;
                break;
            case StructuredIf branch:
                blocks.Add(branch.Condition.Block);
                children = [branch.WhenTrue, branch.WhenFalse];
                break;
            case StructuredLoop loop:
                blocks.Add(loop.Condition.Block);
                children = [loop.Body, loop.ContinueBody, loop.ExitBody];
                break;
            case StructuredPostTestLoop loop:
                blocks.Add(loop.Condition.Block);
                children = [loop.Body, loop.ContinueBody, loop.ExitBody];
                break;
            case StructuredDispatcher dispatcher:
                blocks.UnionWith(dispatcher.Blocks.Select(block => block.Occurrence.Block));
                children = dispatcher.Exits.Select(exit => exit.Body);
                break;
            case StructuredLoopBreak or StructuredLoopContinue or StructuredDispatcherContinue:
                return true;
            default:
                return false;
        }
        return children.All(child => Collect(child, blocks));
    }
}
