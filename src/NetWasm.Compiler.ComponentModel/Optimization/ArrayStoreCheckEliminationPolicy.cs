namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum ArrayStoreCheckEliminationPolicy
{
    Disabled,
    // A reference store into an array whose element type is exact skips the
    // covariant store check: a sealed element class, or an array from newarr.
    ExactElementTypes,
}
