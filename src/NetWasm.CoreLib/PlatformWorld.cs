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
    }
}
