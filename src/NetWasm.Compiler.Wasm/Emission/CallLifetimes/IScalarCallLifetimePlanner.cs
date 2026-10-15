using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.CallLifetimes;

internal interface IScalarCallLifetimePlanner
{
    ScalarCallLifetimePlan Plan(StructuredMethod method, bool enabled);
}
