using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.StaticArrays;

internal interface IValueConstructorPurityClassifier
{
    bool IsPure(MethodInstanceModel method);
}
