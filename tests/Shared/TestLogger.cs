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

        public void Log<State>(
            LogLevel level,
            EventId eventId,
            State state,
            Exception? ex,
            Func<State, Exception?, string> formatter)
        {
            Entries.Add(new(
                level,
                eventId,
                new Dictionary<string, object?>(
                    state as IEnumerable<KeyValuePair<string, object?>> ??
                    Enumerable.Empty<KeyValuePair<string, object?>>()),
                formatter(state, ex),
                ex));
        }

        public bool IsEnabled(LogLevel level) => true;

        public IDisposable BeginScope<State>(State state) => null!;
    };

    public class TestLoggerFactory : ILoggerFactory
    {
        public TestLogger? Logger;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string category) => Logger ??= new TestLogger(category);

        public void Dispose() => GC.SuppressFinalize(this);
    }
}
