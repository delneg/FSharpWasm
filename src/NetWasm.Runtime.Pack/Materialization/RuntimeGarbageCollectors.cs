namespace NetWasm.Runtime.Pack.Materialization;

internal static class RuntimeGarbageCollectors
{
    public const string Compact = "Compact";
    public const string NetWasm = "NetWasm";
    public const string Boehm = "Boehm";
    public const string Bump = "Bump";
    public const string ValidationMessage =
        "The NetWasm garbage collector must be Compact, NetWasm, Boehm or Bump.";
    public const int TargetCount = 8;

    public static bool IsSupported(string? value) =>
        value is Compact or NetWasm or Boehm or Bump;
}
