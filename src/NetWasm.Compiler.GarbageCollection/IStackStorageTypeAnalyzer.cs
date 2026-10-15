using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.GarbageCollection;

public interface IStackStorageTypeAnalyzer
{
    StackStorageAnalysis Analyze(ManagedMethodBody method);
}
