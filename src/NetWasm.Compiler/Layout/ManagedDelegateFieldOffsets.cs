namespace NetWasm.Compiler.Layout;

internal sealed record ManagedDelegateFieldOffsets(
    int Target,
    int MethodId,
    int InvocationList,
    int InvocationCount);
