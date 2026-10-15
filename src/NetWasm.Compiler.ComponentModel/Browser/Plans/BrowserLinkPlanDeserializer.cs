using System;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public interface IBrowserLinkPlanDeserializer
{
    BrowserLinkPlan Deserialize(string json);
}

public sealed class BrowserLinkPlanDeserializer : IBrowserLinkPlanDeserializer
{
    public BrowserLinkPlan Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize(json, BrowserLinkPlanJsonContext.Default.BrowserLinkPlan)
            ?? throw new JsonException("A browser link plan must be an object.");
    }
}
