using System.Runtime.InteropServices.JavaScript;

public static partial class PageExports
{
    [JSImport("show", "app.host")]
    private static extern void Show(string message);

    [JSExport("greet")]
    public static string Greet(string name)
    {
        var message = $"Hello, {name}, from C#.";
        Show(message);
        return message;
    }
}
