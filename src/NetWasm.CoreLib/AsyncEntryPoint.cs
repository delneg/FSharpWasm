namespace System.Runtime.InteropServices
{
    using Threading.Tasks;

    // The synchronous entry a command world gives an asynchronous Main, after Mono's
    // AsyncHelpers.HandleAsyncEntryPoint on WASI: the program drives its own event loop until
    // Main's task completes, then observes the task with await semantics, so a fault, a
    // cancellation or an exit code surfaces exactly as a synchronous Main's would. The compiler
    // calls it with Main's task when the selected world has no host-driven reactor.
    internal static class AsyncEntryPoint
    {
        internal static void Run(Task task)
        {
            PlatformServices.RunEntryPointToCompletion(task);
            task.GetAwaiter().GetResult();
        }

        internal static int Run(Task<int> task)
        {
            PlatformServices.RunEntryPointToCompletion(task);
            return task.GetAwaiter().GetResult();
        }
    }
}
