using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal sealed class ManagedInliningBodyValidator(
    IControlFlowGraphBuilder graphBuilder,
    ITypedStackValidator stackValidator) : IManagedInliningBodyValidator
{
    private readonly IControlFlowGraphBuilder _graphBuilder = graphBuilder ??
        throw new ArgumentNullException(nameof(graphBuilder));
    private readonly ITypedStackValidator _stackValidator = stackValidator ??
        throw new ArgumentNullException(nameof(stackValidator));

    public ImmutableDictionary<int, ImmutableArray<CliValueKind>> Validate(
        CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return _stackValidator.Validate(_graphBuilder.Build(body))
            .InstructionEntryStacks;
    }
}
