// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using NUnit.Framework;
using NUnit.Framework.Internal;
using System;
using System.Collections.Generic;

using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace IceRpc.Tests
{
    /// <summary>LogLevel setting for the LogAttribute. The enum declaration is duplicated here to avoid
    /// having to add a using Microsoft.Extensions.Logging directive in the test source code.</summary>
    public enum LogAttributeLevel
    {
        Trace = LogLevel.Trace,
        Debug = LogLevel.Debug,
        Information = LogLevel.Information,
        Warning = LogLevel.Warning,
        Error = LogLevel.Error,
        Critical = LogLevel.Critical,
        None = LogLevel.None
    }

    /// <summary>LogAttributeLoggerFactory class which delegates the creation of the logger if the log attribute
    /// is set on the test context. A null logger is return otherwise.</summary>
    public sealed class LogAttributeLoggerFactory : ILoggerFactory
    {
        public static LogAttributeLoggerFactory Instance = new();

        private readonly List<ILoggerProvider> _providers = new();

        void ILoggerFactory.AddProvider(ILoggerProvider provider) => _providers.Add(provider);

        ILogger ILoggerFactory.CreateLogger(string categoryName)
        {
            // The Log attribute can either be applied on the test method or test fixture. Using the NUnit.Internal
            // TestExecutionContext is unfortunately the only way to check the test fixture properties (instead of
            // using TestContext.CurrentContext.Test.Properties.Get("Log") which only checks for the test method
            // properties)
            for (Test? test = TestExecutionContext.CurrentContext.CurrentTest; test != null; test = test.Parent as Test)
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

        public void Dispose()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class LogAttribute : PropertyAttribute
    {
        public LogAttributeLevel LogLevel { get; }

        /// <summary>
        /// Construct a LogAttribute with the given log level.
        /// </summary>
        /// <param name="logLevel">The log level</param>
        public LogAttribute(LogAttributeLevel logLevel)
            : base(logLevel)
        {
            Runtime.DefaultLoggerFactory = LogAttributeLoggerFactory.Instance;
            LogLevel = logLevel;
        }
    }
}
