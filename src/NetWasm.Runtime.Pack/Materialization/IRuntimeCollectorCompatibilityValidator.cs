namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeCollectorCompatibilityValidator
{
    void Validate(RuntimePackTarget target, RuntimeLayout layout);
}
