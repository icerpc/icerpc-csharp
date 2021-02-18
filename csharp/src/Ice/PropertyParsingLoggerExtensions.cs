// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace ZeroC.Ice
{
    internal static class PropertyParsingLoggerExtensions
    {
        private static readonly Action<ILogger, string, Exception> _deprecatedProperty = LoggerMessage.Define<string>(
            LogLevel.Warning,
            GetEventId(Event.DeprecatedProperty),
            "deprecated property {Property}");

        private static readonly Action<ILogger, string, string, Exception> _deprecatedPropertyBy =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                GetEventId(Event.DeprecatedProperty),
                "deprecated property {DeprecatedProperty} deprecated by: {NewProperty}");

        private static readonly Action<ILogger, string, Exception> _unknownProperty = LoggerMessage.Define<string>(
            LogLevel.Warning,
            GetEventId(Event.UnknownProperty),
            "unknown property {Property}");

        private static readonly Action<ILogger, string, IReadOnlyList<string>, Exception> _unknownProxyProperty =
            LoggerMessage.Define<string, IReadOnlyList<string>>(
                LogLevel.Warning,
                GetEventId(Event.UnknownProperty),
                "found unknown properties {Properties} for proxy {Proxy}");

        internal static void LogDeprecatedProperty(this ILogger logger, string property) =>
            _deprecatedProperty(logger, property, null!);

        internal static void LogDeprecatedPropertyBy(this ILogger logger, string deprecatedProperty, string newProperty) =>
            _deprecatedPropertyBy(logger, deprecatedProperty, newProperty, null!);

        internal static void LogUnknownProperty(this ILogger logger, string property) =>
            _unknownProperty(logger, property, null!);

        internal static void LogUnknownProxyProperty(this ILogger logger, string proxy, IReadOnlyList<string> properties) =>
            _unknownProxyProperty(logger, proxy, properties, null!);

        private static EventId GetEventId(Event e) => new EventId((int)e, e.ToString());

        private enum Event
        {
            DeprecatedProperty,
            DeprecatedPropertyBy,
            UnknownProperty,
            UnknownProxyProperty,
        }
    }
}
