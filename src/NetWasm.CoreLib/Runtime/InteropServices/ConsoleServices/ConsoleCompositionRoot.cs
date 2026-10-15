namespace System.Runtime.InteropServices.ConsoleServices;

internal static class ConsoleCompositionRoot
{
    internal static IO.TextWriter CreateStandardOutputWriter() =>
        new ConsoleTextWriter(PlatformServices.Streams.GetStandardOutput());

    internal static IO.TextWriter CreateStandardErrorWriter() =>
        new ConsoleTextWriter(PlatformServices.Streams.GetStandardError());
}
