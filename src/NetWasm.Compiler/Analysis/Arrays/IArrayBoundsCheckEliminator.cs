using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal interface IArrayBoundsCheckEliminator
{
    CilMethodBody Rewrite(CilMethodBody body);
}
