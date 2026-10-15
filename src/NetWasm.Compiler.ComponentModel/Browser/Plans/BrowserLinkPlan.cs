using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

/// <summary>A versioned, ordered core-link contract. Hosts must preflight the entire plan before execution.</summary>
public sealed record BrowserLinkPlan(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] ImmutableArray<string> RequiredCapabilities,
    [property: JsonRequired] ImmutableArray<string> Inputs,
    [property: JsonRequired] ImmutableArray<string> Outputs,
    [property: JsonRequired] ImmutableArray<string> TemporaryPaths,
    [property: JsonRequired] ImmutableArray<BrowserLinkOperation> Operations)
{
    public const int CurrentSchemaVersion = 1;
}
