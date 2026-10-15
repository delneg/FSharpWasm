namespace NetWasm.Compiler.ComponentModel.Optimization;

public interface IManagedInliningHintsParser
{
    WasmOptimizationOverrides? Parse(string? value);
}
