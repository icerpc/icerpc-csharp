// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using NUnit.Framework;
using NUnit.Framework.Internal;

using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace IceRpc.Tests.Common;

/// <summary>LogLevel setting for the LogAttribute. The enum declaration is duplicated here to avoid
/// having to add a using Microsoft.Extensions.Logging directive in the test source code.</summary>
public enum LogAttributeLevel
{
    /// <summary>Equivalent to <see cref="LogLevel.Trace"/>.</summary>
    Trace = LogLevel.Trace,
    /// <summary>Equivalent to <see cref="LogLevel.Debug"/>.</summary>
    Debug = LogLevel.Debug,
    /// <summary>Equivalent to <see cref="LogLevel.Information"/>.</summary>
    Information = LogLevel.Information,
    /// <summary>Equivalent to <see cref="LogLevel.Warning"/>.</summary>
    Warning = LogLevel.Warning,
    /// <summary>Equivalent to <see cref="LogLevel.Error"/>.</summary>
    Error = LogLevel.Error,
    /// <summary>Equivalent to <see cref="LogLevel.Critical"/>.</summary>
    Critical = LogLevel.Critical,
    /// <summary>Equivalent to <see cref="LogLevel.None"/>.</summary>
    None = LogLevel.None
}

/// <summary>LogAttributeLoggerFactory class which delegates the creation of the logger if the log attribute
/// is set on the test context. A null logger is return otherwise.</summary>
public sealed class LogAttributeLoggerFactory : ILoggerFactory
{
    /// <summary>The LogAttributeLoggerFactory logger singleton instance.</summary>
    public static LogAttributeLoggerFactory Instance = new();

    /// <inheritdoc/>
    public ILogger Logger => CreateLogger("Test");

    private readonly List<ILoggerProvider> _providers = new();

    /// <inheritdoc/>
    public void AddProvider(ILoggerProvider provider) => _providers.Add(provider);

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        // The Log attribute can either be applied on the test method or test fixture. Using the NUnit.Internal
        // TestExecutionContext is unfortunately the only way to check the test fixture properties (instead of
        // using TestContext.CurrentContext.Test.Properties.Get("Log") which only checks for the test method
        // properties)
        for (Test? test = TestExecutionContext.CurrentContext.CurrentTest; test is not null; test = test.Parent as Test)
        {
            if (test.Properties.Get("Log") is LogAttributeLevel logLevel)
            {
                using ILoggerFactory loggerFactory = LoggerFactory.Create(
                    builder =>
                    {
                        builder.AddSimpleConsole(configure =>
                        {
                            configure.IncludeScopes = true;
                            configure.TimestampFormat = "[HH:mm:ss:fff] ";
                            configure.ColorBehavior = LoggerColorBehavior.Disabled;
                        });
                        builder.SetMinimumLevel((LogLevel)logLevel);
                    });
                foreach (ILoggerProvider provider in _providers)
                {
                    loggerFactory.AddProvider(provider);
                }
                return loggerFactory.CreateLogger($"{test.Name}.{categoryName}");
            }
        }
        return NullLogger.Instance;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>An attribute used by tests classes and methods to enable logging.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class LogAttribute : PropertyAttribute
{
    /// <summary>The log level.</summary>
    public LogAttributeLevel LogLevel { get; }

    /// <summary>Construct a LogAttribute with the given log level.</summary>
    /// <param name="logLevel">The log level</param>
    public LogAttribute(LogAttributeLevel logLevel)
        : base(logLevel) => LogLevel = logLevel;
}
