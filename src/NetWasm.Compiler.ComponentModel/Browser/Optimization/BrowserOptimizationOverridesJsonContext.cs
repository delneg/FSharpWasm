using System.Text.Json.Serialization;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel.Browser.Optimization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(WasmOptimizationOverrides))]
internal sealed partial class BrowserOptimizationOverridesJsonContext : JsonSerializerContext;
