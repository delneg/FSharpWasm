using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal interface IPureReferenceComparisonClassifier
{
    ReferenceComparison? Classify(MethodInstanceModel method);
}
