using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticProgressReporterTests
{
    [Fact]
    public void ReportLogsTheCompletedStage()
    {
        var logger = new RecordingLogger();
        var reporter = Assert.IsAssignableFrom<ICompilerProgressReporter>(
            new CompilerDiagnosticProgressReporter(logger));

        reporter.Report(CompilerProgressStage.Analysis);

        Assert.Equal(LogLevel.Information, logger.Level);
        Assert.Equal(4002, logger.EventId.Id);
        Assert.Contains(nameof(CompilerProgressStage.Analysis), logger.Message);
        Assert.Null(logger.Exception);
    }

    [Fact]
    public void ReportRejectsAnUndefinedStageBeforeLogging()
    {
        var logger = new RecordingLogger();
        var reporter = Assert.IsAssignableFrom<ICompilerProgressReporter>(
            new CompilerDiagnosticProgressReporter(logger));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            reporter.Report((CompilerProgressStage)int.MaxValue));

        Assert.Equal(0, logger.CallCount);
    }

    [Fact]
    public void ConstructorRejectsANullLogger()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticProgressReporter(null!));
    }

    private sealed class RecordingLogger : ILogger<CompilerDiagnosticProgressReporter>
    {
        public int CallCount { get; private set; }

        public LogLevel Level { get; private set; }

        public EventId EventId { get; private set; }

        public string? Message { get; private set; }

        public Exception? Exception { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            CallCount++;
            Level = logLevel;
            EventId = eventId;
            Message = formatter(state, exception);
            Exception = exception;
        }
    }
}
