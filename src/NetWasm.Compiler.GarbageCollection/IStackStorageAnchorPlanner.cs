using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public interface IStackStorageAnchorPlanner
{
    StackStorageAnchorPlan Plan(StackStorageAnchorRequest request);
}
