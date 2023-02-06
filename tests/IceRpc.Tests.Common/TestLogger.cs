// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace IceRpc.Tests.Common;

public sealed record TestLoggerEntry(
    LogLevel LogLevel,
    EventId EventId,
    Dictionary<string, object?> State,
    Dictionary<string, object?> Scope,
    string Message,
    Exception? Exception);

public class TestLogger : ILogger
{
    public string Category { get; }

    public Dictionary<string, object?> CurrentScope { get; internal set; } = new();

    public Channel<TestLoggerEntry> Entries = Channel.CreateUnbounded<TestLoggerEntry>();

    public TestLogger(string category) => Category = category;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Writer.TryWrite(new(
            logLevel,
            eventId,
            new Dictionary<string, object?>(
                state as IEnumerable<KeyValuePair<string, object?>> ??
                Enumerable.Empty<KeyValuePair<string, object?>>()),
            CurrentScope,
            formatter(state, exception),
            exception));
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        CurrentScope = new Dictionary<string, object?>(
            state as IEnumerable<KeyValuePair<string, object?>> ??
            Enumerable.Empty<KeyValuePair<string, object?>>());

        return new Scope(this);
    }

    private sealed class Scope : IDisposable
    {
        private readonly TestLogger _logger;

        public void Dispose() => _logger.CurrentScope = new();

        internal Scope(TestLogger logger) => _logger = logger;
    }
};

public sealed class TestLoggerFactory : ILoggerFactory
{
    public TestLogger? Logger { get; private set; }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => Logger ??= new TestLogger(categoryName);

    public void Dispose() => GC.SuppressFinalize(this);
}
