using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public interface IManagedByRefEffectAnalyzer
{
    ManagedByRefEffects Analyze(ManagedByRefEffectAnalysisRequest request);
}
