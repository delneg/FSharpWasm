using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IStaticDataViewResolver
{
    CilOperand.ReadOnlySpanData? Resolve(MethodInstanceModel producer);
}
