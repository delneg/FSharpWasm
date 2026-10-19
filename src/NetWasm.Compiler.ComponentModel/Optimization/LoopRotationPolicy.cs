namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LoopRotationPolicy
{
    Disabled,
    // A loop tests its condition once before entry and again at the bottom
    // of each iteration, so the body falls through and only the back edge
    // is taken.
    Guarded,
}
