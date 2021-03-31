// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging messages in "IceRpc" category.</summary>
    internal static class LoggerExtensions
    {
        internal const int ServerBaseEventId = 1 * EventIdRange;
        internal const int ProtocolBaseEventId = 2 * EventIdRange;
        internal const int TransportBaseEventId = 3 * EventIdRange;
        internal const int WebSocketBaseEventId = 4 * EventIdRange;
        internal const int SlicBaseEventId = 5 * EventIdRange;
        internal const int OtherBaseEventId = 6 * EventIdRange;
        private const int EventIdRange = 128;

        private const int DeprecatedProperty = OtherBaseEventId + 0;
        private const int DeprecatedPropertyBy = OtherBaseEventId + 1;
        private const int SlicingUnknownType = OtherBaseEventId + 2;
        private const int UnknownProperty = OtherBaseEventId + 3;
        private const int UnknownProxyProperty = OtherBaseEventId + 4;
        private const int WarnProxySecureOptionHasNoEffect = OtherBaseEventId + 5;
        private const int WarnDeprecatedProperty = OtherBaseEventId + 6;

        private static readonly Action<ILogger, string, Exception> _deprecatedProperty =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(DeprecatedProperty, nameof(DeprecatedProperty)),
                "deprecated property {Property}");

        private static readonly Action<ILogger, string, string, Exception> _deprecatedPropertyBy =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(DeprecatedPropertyBy, nameof(DeprecatedPropertyBy)),
                "deprecated property {DeprecatedProperty} deprecated by: {NewProperty}");

        private static readonly Action<ILogger, string, string, Exception> _slicingUnknowType =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(SlicingUnknownType, nameof(SlicingUnknownType)),
                "slicing unknown {Kind} type `{PrintableId}'");

        private static readonly Action<ILogger, string, Exception> _unknownProperty =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(UnknownProperty, nameof(UnknownProperty)),
                "unknown property {Property}");

        private static readonly Action<ILogger, string, IReadOnlyList<string>, Exception> _unknownProxyProperty =
            LoggerMessage.Define<string, IReadOnlyList<string>>(
                LogLevel.Warning,
                new EventId(UnknownProxyProperty, nameof(UnknownProxyProperty)),
                "found unknown properties {Properties} for proxy {Proxy}");

        private static readonly Action<ILogger, string, Exception> _warnProxySecureOptionHasNoEffect =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(WarnProxySecureOptionHasNoEffect, nameof(WarnProxySecureOptionHasNoEffect)),
                "warning while parsing {Proxy}: the -s proxy option no longer has any effect");

        private static readonly Action<ILogger, string, Exception> _warnDeprecatedProperty =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(WarnDeprecatedProperty, nameof(WarnDeprecatedProperty)),
                "deprecated property {Property}");

        internal static void LogDeprecatedProperty(this ILogger logger, string property) =>
            _deprecatedProperty(logger, property, null!);

        internal static void LogDeprecatedPropertyBy(
            this ILogger logger,
            string deprecatedProperty,
            string newProperty) =>
            _deprecatedPropertyBy(logger, deprecatedProperty, newProperty, null!);

        internal static void LogSlicingUnknownType(this ILogger logger, string kind, string printableId) =>
            _slicingUnknowType(logger, kind, printableId, null!);

        internal static void LogUnknownProperty(this ILogger logger, string property) =>
            _unknownProperty(logger, property, null!);

        internal static void LogUnknownProxyProperty(
            this ILogger logger,
            string proxy,
            IReadOnlyList<string> properties) =>
            _unknownProxyProperty(logger, proxy, properties, null!);

        internal static void LogWarnDeprecatedProperty(this ILogger logger, string property) =>
            _warnDeprecatedProperty(logger, property, null!);

        internal static void LogWarnProxySecureOptionHasNoEffect(this ILogger logger, string proxy) =>
            _warnProxySecureOptionHasNoEffect(logger, proxy, null!);

    }
}
