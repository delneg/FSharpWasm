using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Analysis;

internal sealed class ManagedByRefEffectAnalyzerFactory :
    IManagedByRefEffectAnalyzerFactory
{
    public IManagedByRefEffectAnalyzer Create(
        IMethodRepository methods,
        IRuntimeIntrinsicRegistry intrinsics) =>
        new ManagedByRefEffectAnalyzer(methods, intrinsics);
}
