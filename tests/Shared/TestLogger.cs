// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Tests
{
    public sealed class TestLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentDictionary<string, TestLogger> _loggers = new();
        private IExternalScopeProvider? _scopeProvider;
        public JsonFormatter Formatter { get; set; }
        public TextWriter Output { get; set; }

        public TestLoggerProvider(bool indented, TextWriter? writer)
        {
            Formatter = new JsonFormatter()
            {
                Options = new JsonFormatterOptions()
                {
                    JsonWriterOptions = new()
                    {
                        Indented = indented
                    }
                }
            };
            Output = writer ?? Console.Out;
        }

        public ILogger CreateLogger(string categoryName) =>
            _loggers.GetOrAdd(categoryName, name => new TestLogger(name, Formatter, Output, _scopeProvider));

        public void Dispose() => _loggers.Clear();

        /// <inheritdoc />
        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;

            foreach ((string _, TestLogger logger) in _loggers)
            {
                logger.ScopeProvider = _scopeProvider;
            }
        }
    }

    public class TestLogger : ILogger
    {
        private readonly JsonFormatter _formatter;
        private readonly string _name;
        private readonly TextWriter _output;
        internal IExternalScopeProvider? ScopeProvider { get; set; }

        public TestLogger(
            string name,
            JsonFormatter formatter,
            TextWriter output,
            IExternalScopeProvider? scopeProvider)
        {
            _name = name;
            _formatter = formatter;
            _output = output;
            ScopeProvider = scopeProvider;
        }

        public IDisposable? BeginScope<TState>(TState state) => ScopeProvider?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var writer = new StringWriter();
            LogEntry<TState> logEntry = new LogEntry<TState>(logLevel, _name, eventId, state, exception, formatter);
            _formatter.Write(in logEntry, ScopeProvider, writer);
            _output.Write(writer.ToString());
            _output.Flush();
        }
    }

    public class JsonFormatterOptions
    {
        public JsonFormatterOptions() { }

        /// <summary>Gets or sets JsonWriterOptions.</summary>
        public JsonWriterOptions JsonWriterOptions { get; set; }
    }

    public class JsonFormatter
    {
        public JsonFormatterOptions Options { get; set; }

        public JsonFormatter()
        {
            Options = new JsonFormatterOptions();
        }

        public void Write<TState>(
            in LogEntry<TState> logEntry,
            IExternalScopeProvider? scopeProvider,
            TextWriter textWriter)
        {
            string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
            if (logEntry.Exception == null && message == null)
            {
                return;
            }
            LogLevel logLevel = logEntry.LogLevel;
            string category = logEntry.Category;
            int eventId = logEntry.EventId.Id;
            Exception? exception = logEntry.Exception;

            using var output = new MemoryStream();
            using var writer = new Utf8JsonWriter(output, Options.JsonWriterOptions);
            writer.WriteStartObject();
            writer.WriteNumber(nameof(logEntry.EventId), eventId);
            writer.WriteString(nameof(logEntry.LogLevel), GetLogLevelString(logLevel));
            writer.WriteString(nameof(logEntry.Category), category);
            writer.WriteString("Message", message);

            if (exception != null)
            {
                string exceptionMessage = exception.ToString();
                if (!Options.JsonWriterOptions.Indented)
                {
                    exceptionMessage = exceptionMessage.Replace(Environment.NewLine, " ");
                }
                writer.WriteString(nameof(Exception), exceptionMessage);
            }

            if (logEntry.State != null)
            {
                writer.WriteStartObject(nameof(logEntry.State));
                writer.WriteString("Message", logEntry.State.ToString());
                if (logEntry.State is IReadOnlyCollection<KeyValuePair<string, object>> stateProperties)
                {
                    foreach (KeyValuePair<string, object> item in stateProperties)
                    {
                        WriteItem(writer, item);
                    }
                }
                writer.WriteEndObject();
            }
            if (scopeProvider != null)
            {
                WriteScopeInformation(writer, scopeProvider);
            }
            writer.WriteEndObject();
            writer.Flush();
            textWriter.Write(System.Text.Encoding.UTF8.GetString(output.ToArray()));

            textWriter.Write(Environment.NewLine);
        }

        private static string GetLogLevelString(LogLevel logLevel) =>
            logLevel switch
            {
                LogLevel.Trace => "Trace",
                LogLevel.Debug => "Debug",
                LogLevel.Information => "Information",
                LogLevel.Warning => "Warning",
                LogLevel.Error => "Error",
                LogLevel.Critical => "Critical",
                _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
            };

        private static void WriteScopeInformation(Utf8JsonWriter writer, IExternalScopeProvider scopeProvider)
        {
            writer.WriteStartArray("Scopes");
            scopeProvider.ForEachScope((scope, state) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> scopes)
                {
                    state.WriteStartObject();
                    state.WriteString("Message", scope.ToString());
                    foreach (KeyValuePair<string, object> item in scopes)
                    {
                        WriteItem(state, item);
                    }
                    state.WriteEndObject();
                }
                else
                {
                    state.WriteStringValue(scope.ToString());
                }
            }, writer);
            writer.WriteEndArray();
        }

        private static void WriteItem(Utf8JsonWriter writer, KeyValuePair<string, object> item)
        {
            var key = item.Key;
            switch (item.Value)
            {
                case bool boolValue:
                    writer.WriteBoolean(key, boolValue);
                    break;
                case byte byteValue:
                    writer.WriteNumber(key, byteValue);
                    break;
                case sbyte sbyteValue:
                    writer.WriteNumber(key, sbyteValue);
                    break;
                case char charValue:
                    writer.WriteString(key, MemoryMarshal.CreateSpan(ref charValue, 1));
                    break;
                case decimal decimalValue:
                    writer.WriteNumber(key, decimalValue);
                    break;
                case double doubleValue:
                    writer.WriteNumber(key, doubleValue);
                    break;
                case float floatValue:
                    writer.WriteNumber(key, floatValue);
                    break;
                case int intValue:
                    writer.WriteNumber(key, intValue);
                    break;
                case uint uintValue:
                    writer.WriteNumber(key, uintValue);
                    break;
                case long longValue:
                    writer.WriteNumber(key, longValue);
                    break;
                case ulong ulongValue:
                    writer.WriteNumber(key, ulongValue);
                    break;
                case short shortValue:
                    writer.WriteNumber(key, shortValue);
                    break;
                case ushort ushortValue:
                    writer.WriteNumber(key, ushortValue);
                    break;
                case null:
                    writer.WriteNull(key);
                    break;
                default:
                    writer.WriteString(key, item.Value.ToString());
                    break;
            }
        }
    }
}
