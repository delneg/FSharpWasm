using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public interface IRootSlotAllocator
{
    ImmutableDictionary<RootSource, int> Allocate(
        IReadOnlyList<SafepointRootMap> safepoints);
}
