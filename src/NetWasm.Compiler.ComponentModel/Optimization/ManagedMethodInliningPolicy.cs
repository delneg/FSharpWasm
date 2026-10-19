namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum ManagedMethodInliningPolicy
{
    Disabled,
    // Inlines small callees as .NET's JIT does; trades code size for speed.
    Speed,
}
