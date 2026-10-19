using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

public static class HostExports
{
    [JSImport("label", "app.labels")]
    private static extern string Label(int value);

    [JSExport("twice")]
    public static int Twice(int value) => value * 2;

    [JSExport("label")]
    public static string LabelValue(int value) => Label(value);

    [JSExport("sum")]
    public static long Sum(int[] values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total += value;
        }
        return total;
    }

    [JSExport("later")]
    public static async Task<int> Later(string text)
    {
        await Task.Yield();
        return text.Length + DateTime.UtcNow.Year.ToString().Length;
    }
}
