using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IReachableMethodAnalyzerFactory
{
    IReachableMethodAnalyzer Create(
        IMethodBodyReader methodBodies,
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodSpecializer specializer,
        IManagedMethodOptimizer optimizer,
        IImplicitExceptionDiscovery exceptionDiscovery,
        IReachabilityInstructionAnalyzer instructionAnalyzer);
}
