namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LoopInvariantFieldHoistingPolicy
{
    Disabled,
    Conservative,
    // Conservative, plus a loop that checks an invariant array for null
    // before any other effect makes that check once on entry.
    NullChecks,
}
