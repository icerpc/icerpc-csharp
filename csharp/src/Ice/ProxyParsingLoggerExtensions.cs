// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace ZeroC.Ice
{
    internal static class ProxyParsingLoggerExtensions
    {
        private static readonly Action<ILogger, string, Exception> _warnProxySecureOptionHasNoEffect =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                GetEventId(Event.WarnProxySecureOptionHasNoEffect),
                "warning while parsing {Proxy}: the -s proxy option no longer has any effect");

        private static readonly Action<ILogger, string, Exception> _warnDeprecatedProperty =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                GetEventId(Event.WarnDeprecatedProperty),
                "deprecated property {Property}");

        internal static void LogWarnDeprecatedProperty(this ILogger logger, string property) =>
            _warnDeprecatedProperty(logger, property, null!);
        internal static void LogWarnProxySecureOptionHasNoEffect(this ILogger logger, string proxy) =>
            _warnProxySecureOptionHasNoEffect(logger, proxy, null!);

        private static EventId GetEventId(Event e) => new EventId((int)e, e.ToString());

        private enum Event
        {
            WarnProxySecureOptionHasNoEffect,
            WarnDeprecatedProperty,
        }
    }
}
