namespace NetWasm.Compiler.Wasm.Emission.Values;

internal interface IManagedAggregateValueAnalyzer
{
    ManagedAggregateValueAnalysis Analyze(ManagedAggregateValueAnalysisRequest request);
}
