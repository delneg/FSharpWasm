using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.LoopInvariantFields;

internal sealed record FieldProjection(
    CilInstruction Instruction,
    FieldInstanceModel Field);
