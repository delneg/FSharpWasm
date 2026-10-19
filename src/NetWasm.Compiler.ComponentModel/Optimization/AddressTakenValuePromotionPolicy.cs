namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum AddressTakenValuePromotionPolicy
{
    Disabled,
    Conservative,
    // Conservative, plus aggregates with reference and byref fields, whose
    // fields become typed lanes the root map publishes.
    References,
}
