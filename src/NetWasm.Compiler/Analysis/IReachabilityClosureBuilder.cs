using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IReachabilityClosureBuilder
{
    ReachableProgram Build(
        MethodDefinitionModel? entryPoint,
        IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null,
        ImmutableDictionary<string, CilMethodBody>? rewrittenBodies = null);
}
