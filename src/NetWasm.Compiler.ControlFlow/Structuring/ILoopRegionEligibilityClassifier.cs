namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ILoopRegionEligibilityClassifier
{
    bool Classify(LoopRegionEligibilityRequest request);
}
