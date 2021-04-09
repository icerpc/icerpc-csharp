﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging messages in "IceRpc" category.</summary>
    internal static class LoggerExtensions
    {
        internal const int OtherBaseEventId = 0 * EventIdRange;
        internal const int ProtocolBaseEventId = 1 * EventIdRange;
        internal const int ServerBaseEventId = 2 * EventIdRange;
        internal const int SlicBaseEventId = 3 * EventIdRange;
        internal const int TlsBaseEventId = 4 * EventIdRange;
        internal const int TransportBaseEventId = 5 * EventIdRange;
        internal const int WebSocketBaseEventId = 6 * EventIdRange;
        internal const int LocatorClientBaseEventId = 7 * EventIdRange;
        private const int EventIdRange = 128;

        private const int SlicingUnknownType = OtherBaseEventId + 0;
        private const int UnknownProperty = OtherBaseEventId + 1;
        private const int WarnDeprecatedProperty = OtherBaseEventId + 2;

        private static readonly Action<ILogger, string, string, Exception> _slicingUnknowType =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(SlicingUnknownType, nameof(SlicingUnknownType)),
                "slicing unknown {Kind} type '{PrintableId}'");

        private static readonly Action<ILogger, string, Exception> _unknownProperty =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(UnknownProperty, nameof(UnknownProperty)),
                "unknown property {Property}");

        private static readonly Action<ILogger, string, Exception> _warnDeprecatedProperty =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(WarnDeprecatedProperty, nameof(WarnDeprecatedProperty)),
                "deprecated property {Property}");

        internal static void LogSlicingUnknownType(this ILogger logger, string kind, string printableId) =>
            _slicingUnknowType(logger, kind, printableId, null!);

        internal static void LogUnknownProperty(this ILogger logger, string property) =>
            _unknownProperty(logger, property, null!);

        internal static void LogWarnDeprecatedProperty(this ILogger logger, string property) =>
            _warnDeprecatedProperty(logger, property, null!);
    }
}
