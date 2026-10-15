using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IValueFrameLayoutPlanner
{
    ValueFrameLayout Create(StructuredMethod method, NativeImportPlan? nativeImports = null,
        MemberExecutionPlan? memberExecution = null,
        ManagedByRefMethodEffects? byRefEffects = null,
        AddressTakenScalarPromotionPlan? addressTakenScalarPromotions = null,
        AddressTakenAggregatePromotionPlan? addressTakenAggregatePromotions = null,
        ScalarCallLifetimePlan? scalarCallLifetimes = null);
}
