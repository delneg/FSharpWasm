using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BrowserLinkPlan))]
[JsonSerializable(typeof(BrowserLinkExecutionPlan))]
[ExcludeFromCodeCoverage]
internal sealed partial class BrowserLinkPlanJsonContext : JsonSerializerContext;
