using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IReadOnlySpanDataResolver
{
    CilOperand.ReadOnlySpanData? Resolve(
        MethodInstanceModel? target,
        FieldInstanceModel field);
}
