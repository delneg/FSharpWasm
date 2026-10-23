namespace System.Runtime.InteropServices
{
    using CompilerServices;

    // World-dependent composition switches. The NetWasm compiler replaces every call to these
    // members with the value for the selected WIT world before reachability, in every build
    // configuration, so a program reaches only the services its world can bind. The bodies are
    // the values for code compiled without that rewrite, such as CoreLib's desktop actor tests.
    internal static class PlatformWorld
    {
        // True when the selected world imports netwasm:runtime/reactor-host, whose host wakes the
        // pollable reactor; false for command worlds and when no world is selected.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool ImportsReactorHost() => true;

        // True when the selected world imports wasi:io/poll and the monotonic clock, which a
        // program needs to run its own event loop.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool ImportsEventLoop() => true;

        // True for F# programs (FSharp.Core is in the closed world). An F# entry point cannot be
        // asynchronous, so there a blocking wait on an incomplete task runs the event loop until
        // the task completes, as an asynchronous Main would. Elsewhere it throws, as on Mono.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool BlockingWaitDrivesEventLoop() => false;
    }
}
