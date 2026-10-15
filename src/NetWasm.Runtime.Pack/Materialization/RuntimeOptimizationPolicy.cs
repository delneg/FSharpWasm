namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeOptimizationPolicy(
    string? BinaryenFlag,
    bool PreferNativeSpeed);
