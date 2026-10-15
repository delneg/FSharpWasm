using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.StaticArrays;

internal interface IStaticArrayPopulationPlanner
{
    ImmutableHashSet<int> Plan(ManagedMethodBody method, ImmutableHashSet<EntityKey> observedFields);
}
