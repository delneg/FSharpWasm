namespace NetWasm.Compiler.Analysis.Constants;

internal interface IConstantCallEvaluator
{
    ScalarConstant? Evaluate(ConstantCallEvaluation request);
}
