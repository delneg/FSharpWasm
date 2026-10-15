using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal interface ISimpleValueTypeAccessorCanonicalizer
{
    CilMethodBody Canonicalize(CilMethodBody body);
}
