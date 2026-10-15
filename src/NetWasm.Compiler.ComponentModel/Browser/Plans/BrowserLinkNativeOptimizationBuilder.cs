using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkNativeOptimizationBuilder(IBrowserLinkOperationInspector inspector) : IBrowserLinkNativeToolBuilder
{
    private readonly IBrowserLinkOperationInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    public BrowserLinkToolOperation Build(ImmutableArray<string> arguments)
    {
        if (arguments.IsDefault || arguments.Length < 4 || arguments[^2] != "--output")
            throw new ArgumentException("An optimization invocation requires explicit path operands.", nameof(arguments));
        var operation = new BrowserLinkToolOperation("wasm-opt", arguments, [arguments[0]], [arguments[^1]]);
        _inspector.Inspect(operation);
        return operation;
    }
}
