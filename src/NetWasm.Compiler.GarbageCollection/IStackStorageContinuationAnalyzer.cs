using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.GarbageCollection;

public interface IStackStorageContinuationAnalyzer
{
    StackStorageContinuations Analyze(ManagedMethodBody method);
}
