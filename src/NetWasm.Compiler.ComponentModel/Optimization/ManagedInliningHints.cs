namespace NetWasm.Compiler.ComponentModel.Optimization;

/// <summary>Controls managed annotations, not the backend's heuristic inlining.</summary>
public enum ManagedInliningHints
{
    Auto,
    IgnoreAggressive,
    Preserve,
}
