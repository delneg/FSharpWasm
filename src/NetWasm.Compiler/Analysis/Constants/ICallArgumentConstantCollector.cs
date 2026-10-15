using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal interface ICallArgumentConstantCollector
{
    ImmutableArray<ConstantCallEvaluation> Collect(ManagedMethodBody method);
}
