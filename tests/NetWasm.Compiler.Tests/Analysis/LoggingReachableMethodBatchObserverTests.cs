using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class LoggingReachableMethodBatchObserverTests
{
    [Fact]
    public void ObserveWritesOneAggregateDiagnostic()
    {
        var logger = new CompilerTestLogger<LoggingReachableMethodBatchObserver>();
        var observer = new LoggingReachableMethodBatchObserver(logger);

        observer.Observe(new ReachableMethodBatchObservation(
            3,
            2,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(8),
            TimeSpan.FromMilliseconds(2),
            1));

        Assert.Equal(1, logger.EntryCount);
        Assert.Equal(LogLevel.Debug, logger.Level);
        Assert.Contains("requests=3", logger.Message, StringComparison.Ordinal);
        Assert.Contains("workers=2", logger.Message, StringComparison.Ordinal);
    }

}
