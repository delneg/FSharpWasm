namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimePackManifestParser
{
    RuntimePackManifest Parse(string json);
}
