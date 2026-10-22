using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerLoggerTests
{
    [Fact]
    public void IsEnabledRequiresAnActiveScopeAndNonNoneLevel()
    {
        var reader = new MutableScopeReader();
        var logger = Assert.IsAssignableFrom<ILogger<CompilerLoggerTests>>(
            CreateLogger(reader, out _, out _));

        Assert.False(logger.IsEnabled(LogLevel.Information));
        reader.Scope = new CompilerDiagnosticScope(" ");
        Assert.False(logger.IsEnabled(LogLevel.Information));
        reader.Scope = new CompilerDiagnosticScope("compiler.jsonl");
        Assert.False(logger.IsEnabled(LogLevel.None));
        Assert.True(logger.IsEnabled(LogLevel.Trace));
    }

    [Fact]
    public void LogPublishesTheFormattedEntryAndExceptionDetails()
    {
        var reader = new MutableScopeReader
        {
            Scope = new CompilerDiagnosticScope("compiler.jsonl"),
        };
        var logger = Assert.IsAssignableFrom<ILogger<CompilerLoggerTests>>(
            CreateLogger(reader, out var sink, out _));
        var exception = System.Runtime.ExceptionServices.ExceptionDispatchInfo.SetCurrentStackTrace(new InvalidOperationException("failure"));

        logger.Log(
            LogLevel.Warning,
            new EventId(17, "event"),
            "state",
            exception,
            static (state, error) => $"{state}:{error!.Message}");

        var write = Assert.Single(sink.Writes);
        Assert.Equal("compiler.jsonl", write.Path);
        Assert.Equal(typeof(CompilerLoggerTests).FullName, write.Entry.Category);
        Assert.Equal(LogLevel.Warning, write.Entry.Level);
        Assert.Equal(new EventId(17, "event"), write.Entry.EventId);
        Assert.Equal("state:failure", write.Entry.Message);
        Assert.Equal(typeof(InvalidOperationException).FullName, write.Entry.ExceptionType);
        Assert.Equal("failure", write.Entry.ExceptionMessage);
        Assert.False(string.IsNullOrWhiteSpace(write.Entry.ExceptionStackTrace));
    }

    [Fact]
    public void LogWithoutAnEnabledScopeDoesNotFormatOrWrite()
    {
        var reader = new MutableScopeReader();
        var logger = Assert.IsAssignableFrom<ILogger<CompilerLoggerTests>>(
            CreateLogger(reader, out var sink, out _));
        var formatterCalls = 0;

        string Format(string state, Exception? _)
        {
            formatterCalls++;
            return state;
        }

        logger.Log(LogLevel.Information, new EventId(1), "state", exception: null, Format);
        reader.Scope = new CompilerDiagnosticScope(" ");
        logger.Log(LogLevel.Information, new EventId(2), "state", exception: null, Format);
        reader.Scope = new CompilerDiagnosticScope("compiler.jsonl");
        logger.Log(LogLevel.None, new EventId(3), "state", exception: null, Format);

        Assert.Equal(0, formatterCalls);
        Assert.Empty(sink.Writes);
    }
    [Fact]
    public void LogWithoutAnExceptionPublishesNullExceptionDetails()
    {
        var reader = new MutableScopeReader
        {
            Scope = new CompilerDiagnosticScope("compiler.jsonl"),
        };
        var logger = Assert.IsAssignableFrom<ILogger<CompilerLoggerTests>>(
            CreateLogger(reader, out var sink, out _));

        logger.Log(
            LogLevel.Information,
            new EventId(2),
            "state",
            exception: null,
            static (state, _) => state);

        var write = Assert.Single(sink.Writes);
        Assert.Null(write.Entry.ExceptionType);
        Assert.Null(write.Entry.ExceptionMessage);
        Assert.Null(write.Entry.ExceptionStackTrace);
    }

    [Fact]
    public void LogRejectsANullFormatter()
    {
        var reader = new MutableScopeReader();
        var logger = Assert.IsAssignableFrom<ILogger<CompilerLoggerTests>>(
            CreateLogger(reader, out _, out _));

        Assert.Throws<ArgumentNullException>(() => logger.Log<string>(
            LogLevel.Information,
            new EventId(1),
            "state",
            exception: null,
            formatter: null!));
    }

    [Fact]
    public void BeginScopePushesOnlyCompilerDiagnosticScopes()
    {
        var reader = new MutableScopeReader();
        var logger = Assert.IsAssignableFrom<ILogger<CompilerLoggerTests>>(
            CreateLogger(reader, out _, out var pusher));
        var scope = new CompilerDiagnosticScope("compiler.jsonl");

        using var ordinaryLease = logger.BeginScope("ordinary");
        Assert.Empty(pusher.Scopes);

        var diagnosticLease = logger.BeginScope(scope);
        Assert.Same(scope, Assert.Single(pusher.Scopes));
        Assert.False(pusher.Lease.Disposed);
        Assert.NotNull(diagnosticLease);
        diagnosticLease.Dispose();
        Assert.True(pusher.Lease.Disposed);
    }

    private static CompilerLogger<CompilerLoggerTests> CreateLogger(
        ICompilerDiagnosticScopeReader reader,
        out RecordingSink sink,
        out RecordingScopePusher pusher)
    {
        sink = new RecordingSink();
        pusher = new RecordingScopePusher();
        return new CompilerLogger<CompilerLoggerTests>(sink, pusher, reader);
    }

    private sealed class MutableScopeReader : ICompilerDiagnosticScopeReader
    {
        public CompilerDiagnosticScope? Scope { get; set; }

        public CompilerDiagnosticScope? Read() => Scope;
    }

    private sealed class RecordingSink : ICompilerDiagnosticSink
    {
        public List<CompilerDiagnosticWrite> Writes { get; } = [];

        public void Write(CompilerDiagnosticWrite write) => Writes.Add(write);
    }

    private sealed class RecordingScopePusher : ICompilerDiagnosticScopePusher
    {
        public List<CompilerDiagnosticScope> Scopes { get; } = [];

        public RecordingDisposable Lease { get; } = new();

        public IDisposable Push(CompilerDiagnosticScope scope)
        {
            Scopes.Add(scope);
            return Lease;
        }
    }

    private sealed class RecordingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
