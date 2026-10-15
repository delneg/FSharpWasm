using System;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel.Browser.Optimization;

public interface IBrowserOptimizationOverridesDeserializer
{
    WasmOptimizationOverrides? Deserialize(string json);
}

/// <summary>Adapts browser JSON to the compiler-owned optimization request.</summary>
public sealed class BrowserOptimizationOverridesDeserializer : IBrowserOptimizationOverridesDeserializer
{
    public WasmOptimizationOverrides? Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize(json,
            BrowserOptimizationOverridesJsonContext.Default.WasmOptimizationOverrides);
    }
}
