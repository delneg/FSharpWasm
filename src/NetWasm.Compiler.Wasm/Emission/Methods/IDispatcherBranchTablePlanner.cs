using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IDispatcherBranchTablePlanner
{
    DispatcherBranchTablePlan? Plan(StructuredDispatcher dispatcher);
}
