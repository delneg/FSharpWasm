using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis;

internal interface IReachableProgramBuilderFactory
{
    IReachableProgramBuilder Create(
        IAllocationCapabilityAnalyzer allocationCapabilities,
        IManagedByRefEffectAnalyzer byRefEffects,
        IDelegateBindingPlanner delegateBindings);
}
