// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace IceRpc.Tests.Common;

/// <summary>A record that represent a test logger entry.</summary>
/// <param name="LogLevel">The log level.</param>
/// <param name="EventId">The event id.</param>
/// <param name="State">The entry state.</param>
/// <param name="Scope">The entry scope</param>
/// <param name="Message">The message as rendered by the logger.</param>
/// <param name="Exception">The logged exception, it can be null.</param>
public sealed record TestLoggerEntry(
    LogLevel LogLevel,
    EventId EventId,
    Dictionary<string, object?> State,
    Dictionary<string, object?> Scope,
    string Message,
    Exception? Exception);

/// <summary>A logger that exposes the logged messages as a collection of <see cref="TestLoggerEntry"/>. This logger can be used
/// by tests that need to check the logger output.</summary>
public class TestLogger : ILogger
{
    /// <inheritdoc/>
    public string Category { get; }


    /// <summary>The logger scope.</summary>
    public Dictionary<string, object?> CurrentScope { get; internal set; } = new();


    /// <summary>A <see cref="Channel{TestLoggerEntry}"/> where the logger write each entry that has been logged.
    /// </summary>
    public Channel<TestLoggerEntry> Entries = Channel.CreateUnbounded<TestLoggerEntry>();

    /// <summary>Constructs a test logger instance.</summary>
    /// <param name="category">The logger's category.</param>
    public TestLogger(string category) => Category = category;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
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


/// <summary>An implementation of <see cref="ILoggerFactory"/> for creating <see cref="TestLogger"/> instances.
/// </summary>
public sealed class TestLoggerFactory : ILoggerFactory
{
    /// <summary>Gets the logger.</summary>
    public TestLogger? Logger { get; private set; }

    /// <inheritdoc/>
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => Logger ??= new TestLogger(categoryName);

    /// <inheritdoc/>
    public void Dispose() => GC.SuppressFinalize(this);
}
