// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace IceRpc.Tests
{
    public sealed class TestLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentDictionary<string, TestLogger> _loggers = new();
        private IExternalScopeProvider? _scopeProvider;
        public JsonFormatter Formatter { get; set; }
        public TextWriter Output { get; set; }

        public TestLoggerProvider(bool indented, TextWriter? encoder)
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
            Output = encoder ?? Console.Out;
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

        public IDisposable BeginScope<TState>(TState state) => ScopeProvider?.Push(state) ?? new Disposable();

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var encoder = new StringWriter();
            var logEntry = new LogEntry<TState>(logLevel, _name, eventId, state, exception, formatter);
            _formatter.Write(in logEntry, ScopeProvider, encoder);
            _output.Write(encoder.ToString());
            _output.Flush();
        }

        /// <summary>Dummy disposable type, for use with BeginScope.</summary>
        private class Disposable : IDisposable
        {
            public void Dispose()
            {
            }
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

        public JsonFormatter() => Options = new JsonFormatterOptions();

        public void Write<TState>(
            in LogEntry<TState> logEntry,
            IExternalScopeProvider? scopeProvider,
            TextWriter textWriter)
        {
            string? message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
            if (logEntry.Exception == null && message == null)
            {
                return;
            }
            LogLevel logLevel = logEntry.LogLevel;
            string category = logEntry.Category;
            int eventId = logEntry.EventId.Id;
            Exception? exception = logEntry.Exception;

            using var output = new MemoryStream();
            using var encoder = new Utf8JsonWriter(output, Options.JsonWriterOptions);
            encoder.WriteStartObject();
            encoder.WriteNumber(nameof(logEntry.EventId), eventId);
            encoder.WriteString(nameof(logEntry.LogLevel), GetLogLevelString(logLevel));
            encoder.WriteString(nameof(logEntry.Category), category);
            encoder.WriteString("Message", message);

            if (exception != null)
            {
                string exceptionMessage = exception.ToString();
                if (!Options.JsonWriterOptions.Indented)
                {
                    exceptionMessage = exceptionMessage.Replace(Environment.NewLine, " ");
                }
                encoder.WriteString(nameof(Exception), exceptionMessage);
            }

            if (logEntry.State != null)
            {
                encoder.WriteStartObject(nameof(logEntry.State));
                encoder.WriteString("Message", logEntry.State.ToString());
                if (logEntry.State is IReadOnlyCollection<KeyValuePair<string, object>> stateProperties)
                {
                    foreach (KeyValuePair<string, object> item in stateProperties)
                    {
                        WriteItem(encoder, item);
                    }
                }
                encoder.WriteEndObject();
            }
            if (scopeProvider != null)
            {
                WriteScopeInformation(encoder, scopeProvider);
            }
            encoder.WriteEndObject();
            encoder.Flush();
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

        private static void WriteScopeInformation(Utf8JsonWriter encoder, IExternalScopeProvider scopeProvider)
        {
            encoder.WriteStartArray("Scopes");
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
                else if (scope?.ToString() is string value)
                {
                    state.WriteStringValue(value);
                }
            }, encoder);
            encoder.WriteEndArray();
        }

        private static void WriteItem(Utf8JsonWriter encoder, KeyValuePair<string, object> item)
        {
            switch (item.Value)
            {
                case bool boolValue:
                    encoder.WriteBoolean(item.Key, boolValue);
                    break;
                case byte byteValue:
                    encoder.WriteNumber(item.Key, byteValue);
                    break;
                case sbyte sbyteValue:
                    encoder.WriteNumber(item.Key, sbyteValue);
                    break;
                case char charValue:
                    encoder.WriteString(item.Key, MemoryMarshal.CreateSpan(ref charValue, 1));
                    break;
                case decimal decimalValue:
                    encoder.WriteNumber(item.Key, decimalValue);
                    break;
                case double doubleValue:
                    encoder.WriteNumber(item.Key, doubleValue);
                    break;
                case float floatValue:
                    encoder.WriteNumber(item.Key, floatValue);
                    break;
                case int intValue:
                    encoder.WriteNumber(item.Key, intValue);
                    break;
                case uint uintValue:
                    encoder.WriteNumber(item.Key, uintValue);
                    break;
                case long longValue:
                    encoder.WriteNumber(item.Key, longValue);
                    break;
                case ulong ulongValue:
                    encoder.WriteNumber(item.Key, ulongValue);
                    break;
                case short shortValue:
                    encoder.WriteNumber(item.Key, shortValue);
                    break;
                case ushort ushortValue:
                    encoder.WriteNumber(item.Key, ushortValue);
                    break;
                case null:
                    encoder.WriteNull(item.Key);
                    break;
                default:
                    encoder.WriteString(item.Key, item.Value.ToString());
                    break;
            }
        }
    }
}
