using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface ITerminalBranchValueFrameEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        MethodEmissionContext context,
        TerminalBranchValueFramePlacement placement,
        Func<StructuredBlockOccurrence, MethodEmissionContext, ConditionValue> emitCondition,
        Action<StructuredSequence, MethodEmissionContext> emitBody);
}
