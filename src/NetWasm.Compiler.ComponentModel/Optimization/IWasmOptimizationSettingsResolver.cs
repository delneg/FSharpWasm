namespace NetWasm.Compiler.ComponentModel.Optimization;

public interface IWasmOptimizationSettingsResolver
{
    WasmOptimizationSettings Resolve(
        FinalWasmOptimization preset,
        WasmOptimizationOverrides? options = null);
}
