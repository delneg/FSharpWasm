using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class DiagnosticReachableMethodAnalyzerTests
{
    [Fact]
    public void AnalyzeDelegatesTheRequestAndReturnsTheResult()
    {
        var inner = new ReachableMethodAnalyzerProbe();
        var analyzer = new DiagnosticReachableMethodAnalyzer(
            inner,
            NullLogger<DiagnosticReachableMethodAnalyzer>.Instance);
        var request = new ReachableMethodRequest(ValidationTestData.Instance());

        var result = ((IReachableMethodAnalyzer)analyzer).Analyze(request);

        Assert.Null(result);
        Assert.Same(request, inner.Request);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public void AnalyzeLogsTheMethodAndRethrowsTheOriginalFailure()
    {
        var failure = new InvalidOperationException();
        var inner = new ReachableMethodAnalyzerProbe(failure);
        var logger = new RecordingLogger();
        var analyzer = new DiagnosticReachableMethodAnalyzer(inner, logger);
        var request = new ReachableMethodRequest(ValidationTestData.Instance());

        var observed = Assert.Throws<InvalidOperationException>(() =>
            ((IReachableMethodAnalyzer)analyzer).Analyze(request));

        Assert.Same(failure, observed);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(failure, entry.Exception);
        Assert.Contains(request.Method.ToString(), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorAndAnalyzeRejectNullInputs()
    {
        var inner = new ReachableMethodAnalyzerProbe();
        var logger = NullLogger<DiagnosticReachableMethodAnalyzer>.Instance;
        var analyzer = new DiagnosticReachableMethodAnalyzer(inner, logger);

        Assert.Throws<ArgumentNullException>(() =>
            new DiagnosticReachableMethodAnalyzer(null!, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new DiagnosticReachableMethodAnalyzer(inner, null!));
        Assert.Throws<ArgumentNullException>(() =>
            ((IReachableMethodAnalyzer)analyzer).Analyze(null!));
    }

    private sealed class ReachableMethodAnalyzerProbe(Exception? failure = null) :
        IReachableMethodAnalyzer
    {
        internal int CallCount { get; private set; }
        internal ReachableMethodRequest? Request { get; private set; }

        public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
        {
            CallCount++;
            Request = request;
            if (failure is not null)
            {
                throw failure;
            }

            return null!;
        }
    }

    private sealed class RecordingLogger : ILogger<DiagnosticReachableMethodAnalyzer>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
    }

    private sealed record LogEntry(
        LogLevel Level,
        Exception? Exception,
        string Message);
}
