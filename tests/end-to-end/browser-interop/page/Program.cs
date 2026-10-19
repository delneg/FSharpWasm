using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

public static class PageExports
{
    private static int _counter;
    private static int _heard;
    private static JSSubscription? _listener;

    [JSImport("stamp", "app.bundled")]
    private static extern string Stamp(string value);

    [JSImport("show", "app.host")]
    private static extern void Show(string text);

    [JSImport("shout", "app.host")]
    private static extern Task<string> Shout(string text);

    [JSImport("title", "app.host")]
    private static extern string Title(JSObject element);

    [JSImport("listen", "app.host")]
    private static extern JSSubscription Listen(Action<int> heard);

    [JSImport("reenter", "app.host")]
    private static extern int Reenter(int depth);

    [JSExport("add")]
    public static int Add(int value) => _counter += value;

    [JSExport("echo")]
    public static string? Echo(string? value)
    {
        Show(value ?? "<null>");
        return value is null ? null : Stamp(value);
    }

    [JSExport("scale")]
    public static double[] Scale(double[] values, float factor)
    {
        var scaled = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            scaled[index] = values[index] * factor;
        }
        return scaled;
    }

    [JSExport("describe")]
    public static string Describe(JSObject element)
    {
        using (element)
        {
            return Title(element);
        }
    }

    [JSExport("same")]
    public static JSObject? Same(JSObject? value) => value;

    // Async export results stay scalar; the async import settles with a string.
    [JSExport("later")]
    public static async Task<int> Later(string text) => (await Shout(text) == text.ToUpperInvariant() ? 1000 : 0) + text.Length;

    [JSExport("startListening")]
    public static void StartListening() => _listener = Listen(value => _heard += value);

    [JSExport("heard")]
    public static int Heard() => _heard;

    [JSExport("stopListening")]
    public static void StopListening()
    {
        _listener?.Dispose();
        _listener = null;
    }

    // Each level calls back into this export through the page; past the
    // session's nesting limit the import throws and the caller sees -1.
    [JSExport("nest")]
    public static int Nest(int depth)
    {
        if (depth == 0)
        {
            return 0;
        }
        try
        {
            var inner = Reenter(depth - 1);
            return inner < 0 ? inner : inner + 1;
        }
        catch (JSException)
        {
            return -1;
        }
    }

    [JSExport("fail")]
    public static int Fail() => throw new FormatException("page-probe");

    [JSExport("failLater")]
    public static async Task<int> FailLater()
    {
        await Task.Yield();
        throw new FormatException("page-later");
    }

    [JSExport("now")]
    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JSExport("variable")]
    public static string? Variable(string name) => Environment.GetEnvironmentVariable(name);

    [JSExport("print")]
    public static void Print(string text) => Console.WriteLine(text);
}
