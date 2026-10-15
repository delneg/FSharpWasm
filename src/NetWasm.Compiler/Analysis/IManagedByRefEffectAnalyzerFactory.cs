using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Analysis;

internal interface IManagedByRefEffectAnalyzerFactory
{
    IManagedByRefEffectAnalyzer Create(
        IMethodRepository methods,
        IRuntimeIntrinsicRegistry intrinsics);
}
