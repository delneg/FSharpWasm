using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.StaticArrays;

internal interface IStaticFieldObservationCollector
{
    ImmutableHashSet<EntityKey> Collect(ReachableProgram program, ReachabilityRoots roots);
}
