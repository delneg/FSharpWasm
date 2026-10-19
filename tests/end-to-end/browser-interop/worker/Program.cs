using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

public static class WorkerExports
{
    private static int _counter;

    [JSImport("report", "app.host")]
    private static extern void Report(int value);

    [JSExport("add")]
    public static int Add(int value) => _counter += value;

    [JSExport("task")]
    public static async Task<int> TaskResult(int value)
    {
        var survivor = new byte[4096];
        survivor[0] = (byte)value;
        survivor[^1] = (byte)(value + 1);
        await Task.Yield();
        var collections = GC.CollectionCount(0);
        GC.Collect();
        if (GC.CollectionCount(0) <= collections || survivor[0] != (byte)value
            || survivor[^1] != (byte)(value + 1))
        {
            throw new InvalidOperationException("Worker GC survivor contract failed.");
        }
        Report(value);
        return value + 1;
    }

    [JSExport("valueTask")]
    public static async ValueTask<int> ValueTaskResult(int value)
    {
        await Task.Yield();
        return value + 2;
    }

    [JSExport("fail")]
    public static async Task<int> Fail()
    {
        await Task.Yield();
        throw new FormatException("worker-probe");
    }

    [JSExport("failTask")]
    public static async Task FailTask(int messageKind)
    {
        await Task.Yield();
        throw new FormatException(Message(messageKind));
    }

    [JSExport("failValueTask")]
    public static async ValueTask FailValueTask(int messageKind)
    {
        await Task.Yield();
        throw new FormatException(Message(messageKind));
    }

    [JSExport("failValueTaskResult")]
    public static async ValueTask<int> FailValueTaskResult(int messageKind)
    {
        await Task.Yield();
        throw new FormatException(Message(messageKind));
    }

    private static string? Message(int kind) => kind switch
    {
        0 => "",
        1 => "Unicode Ω 😀",
        2 => "call-specific",
        3 => "first instance",
        4 => "second instance",
        _ => null,
    };

    [JSExport("pending")]
    public static Task<int> Pending() => new TaskCompletionSource<int>().Task;
}
