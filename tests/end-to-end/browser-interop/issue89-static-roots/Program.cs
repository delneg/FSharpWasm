using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

// Issue #89 repro 3: per-call cost of a JS-to-C# callback with many static roots.
public static class Program
{
    public static async Task<int> Main()
    {
        Console.WriteLine($"statics touched: {ManyStatics.Touch()}");
        using var subscription = Host.Register(value => value + 1);
        await new TaskCompletionSource().Task;
        return 0;
    }
}

internal static class Host
{
    [JSImport("register", "repro.host")]
    internal static extern JSSubscription Register(Func<int, int> callback);
}
