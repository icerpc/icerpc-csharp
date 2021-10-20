// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Tests
{
    public sealed record TestLoggerEntry(
        LogLevel LogLevel,
        EventId EventId,
        Dictionary<string, object?> State,
        string Message,
        Exception? Exception);

    public class TestLogger : ILogger
    {
        public string Category { get; }
        public List<TestLoggerEntry> Entries = new();

        public TestLogger(string category) => Category = category;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new(
                logLevel,
                eventId,
                new Dictionary<string, object?>(
                    state as IEnumerable<KeyValuePair<string, object?>> ??
                    Enumerable.Empty<KeyValuePair<string, object?>>()),
                formatter(state, exception),
                exception));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable BeginScope<TState>(TState state) => null!;
    };

    public sealed class TestLoggerFactory : ILoggerFactory
    {
        public TestLogger? Logger;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => Logger ??= new TestLogger(categoryName);

        public void Dispose() => GC.SuppressFinalize(this);
    }
}
