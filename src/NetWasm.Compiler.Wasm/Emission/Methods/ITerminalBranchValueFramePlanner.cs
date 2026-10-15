using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface ITerminalBranchValueFramePlanner
{
    TerminalBranchValueFramePlan? Plan(StructuredMethod method, ValueFrameLayout layout, MethodRootMap roots);
}
