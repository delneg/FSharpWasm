using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class DelegateFunctionAppender(
    IDelegateInvokeFunctionEmitter invokes,
    IManagedMethodFunctionTypeResolver functionTypes) : IDelegateFunctionAppender
{
    public void Append(IList<WasmFunctionDefinition> functions,
        ImmutableArray<MethodInstanceModel> invokeMethods,
        DelegateInvokeTarget invokeTarget,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(invokeTarget);
        ArgumentNullException.ThrowIfNull(functionIndices);

        foreach (var invoke in invokeMethods)
        {
            functions.Add(new(
                $"delegate.invoke<{invoke.DeclaringType.CanonicalName}>",
                functionTypes.Resolve(invoke),
                invokes.Emit(invoke, invokeTarget, functionIndices)));
        }
    }
}
