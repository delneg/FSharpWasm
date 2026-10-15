using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal interface IArrayLoopUnroller
{
    CilMethodBody Rewrite(CilMethodBody body);
}
