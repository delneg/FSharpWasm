using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.LoopInvariantFields;

internal interface IFieldProjectionResolver
{
    FieldProjection? Resolve(
        CliTypeIdentity aggregateType,
        CilInstruction instruction);
}
