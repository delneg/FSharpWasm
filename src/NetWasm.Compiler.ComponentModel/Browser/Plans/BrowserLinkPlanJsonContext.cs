using System.Text.Json.Serialization;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BrowserLinkPlan))]
[JsonSerializable(typeof(BrowserLinkExecutionPlan))]
internal sealed partial class BrowserLinkPlanJsonContext : JsonSerializerContext;
