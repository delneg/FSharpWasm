using System;
using System.Runtime.InteropServices.JavaScript;

// Issue #89 repro 1: a delegate bound directly to a [JSImport] method group.
public static class Program
{
    public static int Main()
    {
        Func<string, string?> resolve = Host.Resolve;
        Host.Report(resolve("x") ?? "<null>");
        return 0;
    }
}

internal static class Host
{
    [JSImport("resolve", "repro.host")]
    internal static extern string? Resolve(string name);

    [JSImport("report", "repro.host")]
    internal static extern void Report(string value);
}
