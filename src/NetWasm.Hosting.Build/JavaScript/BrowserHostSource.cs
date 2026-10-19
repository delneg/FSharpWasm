using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>
/// Source shared by every generated browser host: the WASI Preview 2 shim and
/// in-memory filesystem imports, the platform actions a hosting composition
/// takes, and the MSBuild-selected execution configuration.
/// </summary>
internal static class BrowserHostSource
{
    private static readonly string[] Preview2Members =
    [
        "createFilesystem({ preopens }) {",
        "  if (Object.keys(preopens).length !== 0) throw new TypeError(\"browser preopens require explicit browser capabilities\");",
        "  return createFilesystem({ adapter: new InMemoryFilesystemAdapter(), preopens: Object.freeze({}) });",
        "},",
        "createShim: configuration => new WASIShim(configuration),",
    ];

    private static readonly string[] WebMembers =
    [
        "compileCoreModule: WebAssembly.compile.bind(WebAssembly),",
        "createModuleUrl: bytes => URL.createObjectURL(new Blob([bytes], { type: \"text/javascript\" })),",
        "digest: crypto.subtle.digest.bind(crypto.subtle),",
        "fetch: globalThis.fetch.bind(globalThis),",
        "importModule: url => import(url),",
        "revokeModuleUrl: URL.revokeObjectURL.bind(URL),",
    ];

    public static void AppendImports(StringBuilder source, string preview2ShimRoot)
    {
        source.Append("import { WASIShim } from ")
            .Append(ModuleSpecifier(Path.Combine(preview2ShimRoot, "dist", "common", "instantiation.js")))
            .AppendLine(";");
        source.Append("import { createFilesystem, InMemoryFilesystemAdapter } from ")
            .Append(ModuleSpecifier(Path.Combine(preview2ShimRoot, "dist", "browser", "filesystem.js")))
            .AppendLine(";");
    }

    /// <summary>The export-session and execution platform: Preview 2 and web actions together.</summary>
    public static void AppendPlatformMembers(StringBuilder source, string indent)
    {
        AppendPreview2Members(source, indent);
        AppendWebMembers(source, indent);
    }

    public static void AppendPreview2Members(StringBuilder source, string indent) =>
        AppendLines(source, indent, Preview2Members);

    public static void AppendWebMembers(StringBuilder source, string indent) =>
        AppendLines(source, indent, WebMembers);

    public static void AppendConfiguration(
        StringBuilder source,
        string indent,
        IEnumerable<NetWasmApplicationImport> applicationImports,
        IEnumerable<string> arguments,
        IReadOnlyCollection<WorkerEnvironmentVariable> environment,
        IEnumerable<string> clocks,
        string network,
        bool randomness)
    {
        var imports = applicationImports.ToArray();
        if (imports.Length == 0)
        {
            source.Append(indent).AppendLine("applicationImports: Object.freeze([]),");
        }
        else
        {
            source.Append(indent).AppendLine("applicationImports: Object.freeze([");
            foreach (var binding in imports)
            {
                source.Append(indent).Append("  Object.freeze({ module: ").Append(Json(binding.Module))
                    .Append(", artifactPath: ").Append(Json(binding.ArtifactPath))
                    .Append(", sha256: ").Append(Json(binding.Sha256)).AppendLine(" }),");
            }
            source.Append(indent).AppendLine("]),");
        }
        source.Append(indent).Append("arguments: Object.freeze(").Append(Json(arguments)).AppendLine("),");
        source.Append(indent).AppendLine("environment: Object.freeze([");
        foreach (var variable in environment)
        {
            source.Append(indent).Append("  Object.freeze({ name: ").Append(Json(variable.Name))
                .Append(", value: ").Append(Json(variable.Value)).AppendLine(" }),");
        }
        source.Append(indent).AppendLine("]),");
        source.Append(indent).AppendLine("grants: Object.freeze({");
        source.Append(indent).Append("  clocks: Object.freeze(").Append(Json(clocks)).AppendLine("),");
        source.Append(indent).Append("  environment: Object.freeze(")
            .Append(Json(environment.Select(variable => variable.Name))).AppendLine("),");
        source.Append(indent).Append("  network: ").Append(Json(network)).AppendLine(",");
        source.Append(indent).AppendLine("  preopens: Object.freeze([]),");
        source.Append(indent).Append("  randomness: ").Append(randomness ? "true" : "false").AppendLine(",");
        source.Append(indent).AppendLine("}),");
    }

    public static string ModuleSpecifier(string path) =>
        Json(new Uri(Path.GetFullPath(path)).AbsoluteUri);

    private static void AppendLines(StringBuilder source, string indent, IEnumerable<string> lines)
    {
        foreach (var line in lines) source.Append(indent).AppendLine(line);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
