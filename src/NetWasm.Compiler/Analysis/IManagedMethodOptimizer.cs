using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IManagedMethodOptimizer
{
    CilMethodBody Optimize(ManagedMethodBody method);
}
