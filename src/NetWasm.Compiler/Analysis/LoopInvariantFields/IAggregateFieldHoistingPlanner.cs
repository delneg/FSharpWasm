using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.LoopInvariantFields;

internal interface IAggregateFieldHoistingPlanner
{
    AggregateFieldHoistingPlan Plan(CilMethodBody body);
}
